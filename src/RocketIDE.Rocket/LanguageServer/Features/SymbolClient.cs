using System.IO;
using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record RocketDocumentSymbol(
    string Name,
    string? Detail,
    int Kind,
    string Path,
    LspRange Range,
    LspRange SelectionRange,
    IReadOnlyList<RocketDocumentSymbol> Children);

public sealed record RocketWorkspaceSymbol(
    string Name,
    string? ContainerName,
    int Kind,
    string Path,
    LspRange Range,
    long SnapshotGeneration);

public sealed class SymbolClient(IRocketLanguageClient client)
{
    public async Task<IReadOnlyList<RocketDocumentSymbol>> RequestDocumentSymbolsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var response = await client.RequestAsync<JsonElement>(
            "textDocument/documentSymbol",
            new { textDocument = new { uri = LspFeatureParsing.PathToUri(path) } },
            cancellationToken).ConfigureAwait(false);
        return ParseDocumentSymbols(response, Path.GetFullPath(path));
    }

    public async Task<IReadOnlyList<RocketWorkspaceSymbol>> RequestWorkspaceSymbolsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var response = await client.RequestAsync<JsonElement>(
            "workspace/symbol",
            new { query = query ?? string.Empty },
            cancellationToken).ConfigureAwait(false);
        return ParseWorkspaceSymbols(response);
    }

    internal static IReadOnlyList<RocketDocumentSymbol> ParseDocumentSymbols(JsonElement response, string documentPath)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }
        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException("textDocument/documentSymbol returned an invalid result shape.");
        }

        var items = response.EnumerateArray().ToArray();
        if (items.Length == 0)
        {
            return [];
        }

        var first = items[0];
        if (first.ValueKind != JsonValueKind.Object)
        {
            throw new LspProtocolException("textDocument/documentSymbol returned a malformed symbol.");
        }

        var hierarchical = first.TryGetProperty("selectionRange", out _);
        var flat = first.TryGetProperty("location", out _);
        if (!hierarchical && !flat)
        {
            throw new LspProtocolException("textDocument/documentSymbol returned an unknown symbol shape.");
        }

        var result = new List<RocketDocumentSymbol>(items.Length);
        foreach (var item in items)
        {
            result.Add(hierarchical
                ? ParseDocumentSymbol(item, documentPath, depth: 0)
                : ParseFlatDocumentSymbol(item, documentPath));
        }
        return result;
    }

    internal static IReadOnlyList<RocketWorkspaceSymbol> ParseWorkspaceSymbols(JsonElement response)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new LspProtocolException("workspace/symbol returned no result array.");
        }
        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException("workspace/symbol returned an invalid result shape.");
        }

        if (response.GetArrayLength() > 200)
        {
            throw new LspProtocolException("workspace/symbol exceeded the advertised 200-result bound.");
        }

        var result = new List<RocketWorkspaceSymbol>();
        long? generation = null;
        foreach (var item in response.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new LspProtocolException("workspace/symbol returned a malformed symbol.");
            }

            var name = RequiredString(item, "name", "workspace symbol");
            var kind = RequiredPositiveInt(item, "kind", "workspace symbol");
            var container = OptionalString(item, "containerName");
            if (!item.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("rocketGeneration", out var generationValue) ||
                generationValue.ValueKind != JsonValueKind.Number ||
                !generationValue.TryGetInt64(out var snapshotGeneration) || snapshotGeneration <= 0)
            {
                throw new LspProtocolException("workspace symbol is missing its snapshot generation.");
            }
            if (generation is not null && generation != snapshotGeneration)
            {
                throw new LspProtocolException("workspace symbols contain mixed snapshot generations.");
            }
            generation = snapshotGeneration;
            if (!item.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object)
            {
                throw new LspProtocolException("workspace symbol is missing location.");
            }
            if (!location.TryGetProperty("uri", out var uri) || uri.ValueKind != JsonValueKind.String ||
                !location.TryGetProperty("range", out var rangeElement))
            {
                throw new LspProtocolException("workspace symbol location is malformed.");
            }

            result.Add(new RocketWorkspaceSymbol(
                name,
                container,
                kind,
                NavigationClient.ParseFileUri(uri.GetString()!, "workspace symbol"),
                LspFeatureParsing.ParseRange(rangeElement, "workspace symbol"),
                snapshotGeneration));
        }
        return result;
    }

    private static RocketDocumentSymbol ParseDocumentSymbol(JsonElement item, string documentPath, int depth)
    {
        if (depth > 128)
        {
            throw new LspProtocolException("Document symbol hierarchy is unreasonably deep.");
        }
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new LspProtocolException("Document symbol is malformed.");
        }

        var name = RequiredString(item, "name", "document symbol");
        var kind = RequiredPositiveInt(item, "kind", "document symbol");
        if (!item.TryGetProperty("range", out var rangeElement) ||
            !item.TryGetProperty("selectionRange", out var selectionRangeElement))
        {
            throw new LspProtocolException("Document symbol is missing range or selectionRange.");
        }

        var children = new List<RocketDocumentSymbol>();
        if (item.TryGetProperty("children", out var childrenElement) &&
            childrenElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (childrenElement.ValueKind != JsonValueKind.Array)
            {
                throw new LspProtocolException("Document symbol children must be an array.");
            }
            foreach (var child in childrenElement.EnumerateArray())
            {
                if (children.Count >= 4096)
                {
                    throw new LspProtocolException("Document symbol child count exceeds RocketIDE safety bounds.");
                }
                children.Add(ParseDocumentSymbol(child, documentPath, depth + 1));
            }
        }

        return new RocketDocumentSymbol(
            name,
            OptionalString(item, "detail"),
            kind,
            documentPath,
            LspFeatureParsing.ParseRange(rangeElement, "document symbol"),
            LspFeatureParsing.ParseRange(selectionRangeElement, "document symbol selection"),
            children);
    }

    private static RocketDocumentSymbol ParseFlatDocumentSymbol(JsonElement item, string requestedPath)
    {
        var name = RequiredString(item, "name", "document symbol");
        var kind = RequiredPositiveInt(item, "kind", "document symbol");
        if (!item.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object ||
            !location.TryGetProperty("uri", out var uri) || uri.ValueKind != JsonValueKind.String ||
            !location.TryGetProperty("range", out var rangeElement))
        {
            throw new LspProtocolException("Flat document symbol location is malformed.");
        }

        var path = NavigationClient.ParseFileUri(uri.GetString()!, "document symbol");
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(requestedPath), PathComparison))
        {
            throw new LspProtocolException("textDocument/documentSymbol returned a symbol for a different document.");
        }
        var range = LspFeatureParsing.ParseRange(rangeElement, "document symbol");
        return new RocketDocumentSymbol(name, null, kind, path, range, range, []);
    }

    private static string RequiredString(JsonElement item, string propertyName, string context)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new LspProtocolException($"{context} is missing {propertyName}.");
        }
        return value.GetString()!;
    }

    private static int RequiredPositiveInt(JsonElement item, string propertyName, string context)
    {
        if (!item.TryGetProperty(propertyName, out var value) || !value.TryGetInt32(out var result) || result <= 0)
        {
            throw new LspProtocolException($"{context} contains invalid {propertyName}.");
        }
        return result;
    }

    private static string? OptionalString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
