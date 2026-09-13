using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record RocketLocation(string Path, LspRange Range);
public sealed record RocketPrepareRenameResult(LspRange? Range, string? Placeholder, bool DefaultBehavior);
public sealed record RocketWorkspaceDocumentEdit(string Path, int? Version, IReadOnlyList<RocketTextEdit> Edits);
public sealed record RocketWorkspaceEdit(IReadOnlyList<RocketWorkspaceDocumentEdit> Documents);
public sealed record RocketCodeAction(string Title, string? Kind, RocketWorkspaceEdit? Edit, bool HasUnsupportedCommand, string? DisabledReason = null);
public sealed record RocketCodeActionDiagnostic(
    LspRange Range,
    int? Severity,
    string? Code,
    string? Source,
    string Message,
    JsonElement Data = default);

public sealed class NavigationClient(IRocketLanguageClient client)
{
    public async Task<IReadOnlyList<RocketLocation>> RequestDefinitionAsync(
        string path, LspPosition position, CancellationToken cancellationToken)
    {
        var response = await client.RequestAsync<JsonElement>(
            "textDocument/definition",
            TextDocumentPosition(path, position),
            cancellationToken).ConfigureAwait(false);
        return ParseDefinitionResponse(response);
    }

    public async Task<IReadOnlyList<RocketLocation>> RequestReferencesAsync(
        string path, LspPosition position, CancellationToken cancellationToken)
    {
        var response = await client.RequestAsync<JsonElement>(
            "textDocument/references",
            new
            {
                textDocument = new { uri = LspFeatureParsing.PathToUri(path) },
                position,
                context = new { includeDeclaration = true },
            },
            cancellationToken).ConfigureAwait(false);
        return ParseLocationArrayResponse(response, "textDocument/references");
    }

    public async Task<RocketPrepareRenameResult?> PrepareRenameAsync(
        string path, LspPosition position, CancellationToken cancellationToken)
    {
        var response = await client.RequestAsync<JsonElement>(
            "textDocument/prepareRename",
            TextDocumentPosition(path, position),
            cancellationToken).ConfigureAwait(false);
        return ParsePrepareRenameResponse(response);
    }

    public async Task<RocketWorkspaceEdit?> RequestRenameAsync(
        string path, LspPosition position, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var response = await client.RequestAsync<JsonElement>(
            "textDocument/rename",
            new
            {
                textDocument = new { uri = LspFeatureParsing.PathToUri(path) },
                position,
                newName,
            },
            cancellationToken).ConfigureAwait(false);
        return response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : ParseWorkspaceEdit(response);
    }

    public async Task<IReadOnlyList<RocketCodeAction>> RequestCodeActionsAsync(
        string path,
        LspRange range,
        IReadOnlyList<RocketCodeActionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var response = await client.RequestAsync<JsonElement>(
            "textDocument/codeAction",
            new
            {
                textDocument = new { uri = LspFeatureParsing.PathToUri(path) },
                range,
                context = new
                {
                    diagnostics = diagnostics.Select(SerializeCodeActionDiagnostic).ToArray(),
                    only = new[] { "quickfix" },
                },
            },
            cancellationToken).ConfigureAwait(false);
        return ParseCodeActions(response)
            .Where(action => IsRequestedCodeActionKind(action.Kind, "quickfix"))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> SerializeCodeActionDiagnostic(RocketCodeActionDiagnostic diagnostic)
    {
        var result = new Dictionary<string, object?>
        {
            ["range"] = diagnostic.Range,
            ["severity"] = diagnostic.Severity,
            ["code"] = diagnostic.Code,
            ["source"] = diagnostic.Source,
            ["message"] = diagnostic.Message,
        };
        if (diagnostic.Data.ValueKind != JsonValueKind.Undefined)
        {
            result["data"] = diagnostic.Data;
        }
        return result;
    }

    internal static bool IsRequestedCodeActionKind(string? actualKind, string requestedKind)
    {
        if (string.IsNullOrWhiteSpace(actualKind) || string.IsNullOrWhiteSpace(requestedKind))
        {
            return false;
        }

        return string.Equals(actualKind, requestedKind, StringComparison.Ordinal) ||
            actualKind.StartsWith(requestedKind + ".", StringComparison.Ordinal);
    }

    public async Task<IReadOnlyList<RocketTextEdit>> RequestFormattingAsync(
        string path, int tabSize, bool insertSpaces, CancellationToken cancellationToken)
    {
        if (tabSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tabSize));
        }

        var response = await client.RequestAsync<JsonElement>(
            "textDocument/formatting",
            new
            {
                textDocument = new { uri = LspFeatureParsing.PathToUri(path) },
                options = new { tabSize, insertSpaces },
            },
            cancellationToken).ConfigureAwait(false);
        return ParseTextEdits(response, "textDocument/formatting");
    }

    internal static IReadOnlyList<RocketLocation> ParseDefinitionResponse(JsonElement response)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (response.ValueKind == JsonValueKind.Array)
        {
            var locations = new List<RocketLocation>();
            foreach (var element in response.EnumerateArray())
            {
                locations.Add(ParseDefinitionLocation(element));
            }
            return locations;
        }

        if (response.ValueKind == JsonValueKind.Object)
        {
            return [ParseDefinitionLocation(response)];
        }

        throw new LspProtocolException("textDocument/definition returned an invalid result shape.");
    }

    internal static RocketPrepareRenameResult? ParsePrepareRenameResponse(JsonElement response)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (response.ValueKind != JsonValueKind.Object)
        {
            throw new LspProtocolException("textDocument/prepareRename returned an invalid result shape.");
        }

        if (response.TryGetProperty("defaultBehavior", out var defaultBehavior) && defaultBehavior.ValueKind == JsonValueKind.True)
        {
            return new RocketPrepareRenameResult(null, null, true);
        }

        if (response.TryGetProperty("range", out var rangeElement))
        {
            var placeholder = response.TryGetProperty("placeholder", out var placeholderElement) && placeholderElement.ValueKind == JsonValueKind.String
                ? placeholderElement.GetString()
                : null;
            return new RocketPrepareRenameResult(LspFeatureParsing.ParseRange(rangeElement, "prepareRename"), placeholder, false);
        }

        if (response.TryGetProperty("start", out _) && response.TryGetProperty("end", out _))
        {
            return new RocketPrepareRenameResult(LspFeatureParsing.ParseRange(response, "prepareRename"), null, false);
        }

        throw new LspProtocolException("textDocument/prepareRename returned neither a range nor defaultBehavior.");
    }

    internal static RocketWorkspaceEdit ParseWorkspaceEdit(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            throw new LspProtocolException("WorkspaceEdit must be an object.");
        }

        var documents = new List<RocketWorkspaceDocumentEdit>();
        if (response.TryGetProperty("changes", out var changes) && changes.ValueKind != JsonValueKind.Null)
        {
            if (changes.ValueKind != JsonValueKind.Object)
            {
                throw new LspProtocolException("WorkspaceEdit.changes must be an object.");
            }

            foreach (var property in changes.EnumerateObject())
            {
                documents.Add(new RocketWorkspaceDocumentEdit(
                    ParseFileUri(property.Name, "WorkspaceEdit.changes"),
                    null,
                    ParseTextEdits(property.Value, "WorkspaceEdit.changes")));
            }
        }

        if (response.TryGetProperty("documentChanges", out var documentChanges) && documentChanges.ValueKind != JsonValueKind.Null)
        {
            if (documentChanges.ValueKind != JsonValueKind.Array)
            {
                throw new LspProtocolException("WorkspaceEdit.documentChanges must be an array.");
            }

            foreach (var change in documentChanges.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object)
                {
                    throw new LspProtocolException("WorkspaceEdit.documentChanges contains an invalid entry.");
                }

                if (change.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String)
                {
                    throw new LspProtocolException($"WorkspaceEdit resource operation '{kind.GetString()}' is unsupported in IDE-WP09.");
                }

                if (!change.TryGetProperty("textDocument", out var textDocument) || textDocument.ValueKind != JsonValueKind.Object ||
                    !textDocument.TryGetProperty("uri", out var uriElement) || uriElement.ValueKind != JsonValueKind.String ||
                    !change.TryGetProperty("edits", out var edits))
                {
                    throw new LspProtocolException("WorkspaceEdit text document change is missing textDocument.uri or edits.");
                }

                int? version = null;
                if (textDocument.TryGetProperty("version", out var versionElement) && versionElement.ValueKind != JsonValueKind.Null)
                {
                    if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var parsedVersion) || parsedVersion < 0)
                    {
                        throw new LspProtocolException("WorkspaceEdit document version is invalid.");
                    }
                    version = parsedVersion;
                }

                documents.Add(new RocketWorkspaceDocumentEdit(
                    ParseFileUri(uriElement.GetString()!, "WorkspaceEdit.documentChanges"),
                    version,
                    ParseTextEdits(edits, "WorkspaceEdit.documentChanges")));
            }
        }

        return new RocketWorkspaceEdit(documents);
    }

    internal static IReadOnlyList<RocketCodeAction> ParseCodeActions(JsonElement response)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }
        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException("textDocument/codeAction returned an invalid result shape.");
        }

        var actions = new List<RocketCodeAction>();
        foreach (var element in response.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("title", out var titleElement) || titleElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(titleElement.GetString()))
            {
                throw new LspProtocolException("textDocument/codeAction returned an action without a title.");
            }

            var kind = element.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String
                ? kindElement.GetString()
                : null;
            RocketWorkspaceEdit? edit = null;
            if (element.TryGetProperty("edit", out var editElement) && editElement.ValueKind != JsonValueKind.Null)
            {
                edit = ParseWorkspaceEdit(editElement);
            }

            var hasCommand = element.TryGetProperty("command", out var commandElement) &&
                commandElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            string? disabledReason = null;
            if (element.TryGetProperty("disabled", out var disabledElement) && disabledElement.ValueKind != JsonValueKind.Null)
            {
                if (disabledElement.ValueKind != JsonValueKind.Object ||
                    !disabledElement.TryGetProperty("reason", out var reasonElement) ||
                    reasonElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(reasonElement.GetString()))
                {
                    throw new LspProtocolException("textDocument/codeAction returned a malformed disabled reason.");
                }
                disabledReason = reasonElement.GetString();
            }
            actions.Add(new RocketCodeAction(titleElement.GetString()!, kind, edit, hasCommand, disabledReason));
        }
        return actions;
    }

    internal static IReadOnlyList<RocketTextEdit> ParseTextEdits(JsonElement response, string context)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }
        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException($"{context} edits must be an array.");
        }

        var edits = new List<RocketTextEdit>();
        foreach (var edit in response.EnumerateArray())
        {
            if (edit.ValueKind != JsonValueKind.Object ||
                !edit.TryGetProperty("range", out var range) ||
                !edit.TryGetProperty("newText", out var newText) || newText.ValueKind != JsonValueKind.String)
            {
                throw new LspProtocolException($"{context} contains a malformed text edit.");
            }
            edits.Add(new RocketTextEdit(LspFeatureParsing.ParseRange(range, context), newText.GetString() ?? string.Empty));
        }
        return edits;
    }

    private static IReadOnlyList<RocketLocation> ParseLocationArrayResponse(JsonElement response, string context)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }
        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException($"{context} returned an invalid result shape.");
        }
        return response.EnumerateArray().Select(ParseLocation).ToArray();
    }

    private static RocketLocation ParseDefinitionLocation(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new LspProtocolException("Definition location is not an object.");
        }

        if (element.TryGetProperty("targetUri", out var targetUri))
        {
            if (targetUri.ValueKind != JsonValueKind.String || !element.TryGetProperty("targetSelectionRange", out var selectionRange))
            {
                throw new LspProtocolException("Definition LocationLink is malformed.");
            }
            return new RocketLocation(
                ParseFileUri(targetUri.GetString()!, "definition LocationLink"),
                LspFeatureParsing.ParseRange(selectionRange, "definition LocationLink"));
        }

        return ParseLocation(element);
    }

    private static RocketLocation ParseLocation(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("uri", out var uri) || uri.ValueKind != JsonValueKind.String ||
            !element.TryGetProperty("range", out var range))
        {
            throw new LspProtocolException("LSP location is malformed.");
        }
        return new RocketLocation(
            ParseFileUri(uri.GetString()!, "LSP location"),
            LspFeatureParsing.ParseRange(range, "LSP location"));
    }

    internal static string ParseFileUri(string value, string context)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new LspProtocolException($"{context} URI '{value}' is not a safe absolute file URI.");
        }
        try
        {
            return Path.GetFullPath(uri.LocalPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new LspProtocolException($"{context} URI '{value}' cannot be converted to a normalized file path.", exception);
        }
    }

    private static object TextDocumentPosition(string path, LspPosition position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(position);
        return new { textDocument = new { uri = LspFeatureParsing.PathToUri(path) }, position };
    }
}
