using System.Text.Json;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record RocketFoldingRange(
    int StartLine,
    int? StartCharacter,
    int EndLine,
    int? EndCharacter,
    string? Kind);

public sealed class FoldingClient(IRocketLanguageClient client)
{
    public async Task<IReadOnlyList<RocketFoldingRange>> RequestAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var response = await client.RequestAsync<JsonElement>(
            "textDocument/foldingRange",
            new { textDocument = new { uri = LspFeatureParsing.PathToUri(path) } },
            cancellationToken).ConfigureAwait(false);
        return Parse(response);
    }

    internal static IReadOnlyList<RocketFoldingRange> Parse(JsonElement response)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }
        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException("textDocument/foldingRange returned an invalid result shape.");
        }

        var result = new List<RocketFoldingRange>();
        foreach (var item in response.EnumerateArray())
        {
            if (result.Count >= 4096)
            {
                break;
            }
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("startLine", out var startLineElement) || !startLineElement.TryGetInt32(out var startLine) || startLine < 0 ||
                !item.TryGetProperty("endLine", out var endLineElement) || !endLineElement.TryGetInt32(out var endLine) || endLine < startLine)
            {
                throw new LspProtocolException("textDocument/foldingRange returned a malformed range.");
            }

            var startCharacter = ReadOptionalCharacter(item, "startCharacter");
            var endCharacter = ReadOptionalCharacter(item, "endCharacter");
            if (endLine == startLine && startCharacter.HasValue && endCharacter.HasValue && endCharacter < startCharacter)
            {
                throw new LspProtocolException("Folding range end precedes its start.");
            }
            var kind = item.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String
                ? kindElement.GetString()
                : null;
            result.Add(new RocketFoldingRange(startLine, startCharacter, endLine, endCharacter, kind));
        }
        return result;
    }

    private static int? ReadOptionalCharacter(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }
        if (!value.TryGetInt32(out var character) || character < 0)
        {
            throw new LspProtocolException($"Folding range {propertyName} is invalid.");
        }
        return character;
    }
}
