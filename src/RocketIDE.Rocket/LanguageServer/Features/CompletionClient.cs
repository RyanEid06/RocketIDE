using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record RocketCompletionTextEdit(LspRange InsertRange, LspRange ReplaceRange, string NewText);

public sealed record RocketCompletionItem(
    string Label,
    int? Kind,
    string? Detail,
    RocketMarkupContent? Documentation,
    string InsertText,
    string? FilterText,
    string? SortText,
    RocketCompletionTextEdit? TextEdit,
    IReadOnlyList<RocketTextEdit> AdditionalTextEdits);

public sealed record RocketCompletionResult(IReadOnlyList<RocketCompletionItem> Items, bool IsIncomplete);

public sealed class CompletionClient(IRocketLanguageClient client)
{
    public async Task<RocketCompletionResult?> RequestAsync(
        string path,
        LspPosition position,
        string? triggerCharacter,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(position);

        var parameters = new
        {
            textDocument = new { uri = LspFeatureParsing.PathToUri(path) },
            position,
            context = new
            {
                triggerKind = string.IsNullOrEmpty(triggerCharacter) ? 1 : 2,
                triggerCharacter = string.IsNullOrEmpty(triggerCharacter) ? (string?)null : triggerCharacter,
            },
        };
        var response = await client.RequestAsync<JsonElement>("textDocument/completion", parameters, cancellationToken).ConfigureAwait(false);
        return ParseResponse(response);
    }

    internal static RocketCompletionResult? ParseResponse(JsonElement response)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var isIncomplete = false;
        JsonElement items;
        if (response.ValueKind == JsonValueKind.Array)
        {
            items = response;
        }
        else if (response.ValueKind == JsonValueKind.Object &&
                 response.TryGetProperty("items", out items) &&
                 items.ValueKind == JsonValueKind.Array)
        {
            isIncomplete = response.TryGetProperty("isIncomplete", out var incomplete) && incomplete.ValueKind == JsonValueKind.True;
        }
        else
        {
            throw new LspProtocolException("textDocument/completion returned neither a CompletionItem array nor CompletionList.");
        }

        var mapped = new List<RocketCompletionItem>();
        foreach (var item in items.EnumerateArray())
        {
            mapped.Add(ParseItem(item));
        }

        return new RocketCompletionResult(mapped, isIncomplete);
    }

    private static RocketCompletionItem ParseItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("label", out var labelElement) ||
            labelElement.ValueKind != JsonValueKind.String)
        {
            throw new LspProtocolException("Completion item is missing label.");
        }

        var label = labelElement.GetString() ?? string.Empty;
        var kind = item.TryGetProperty("kind", out var kindElement) && kindElement.TryGetInt32(out var kindValue)
            ? kindValue
            : (int?)null;
        var detail = ReadOptionalString(item, "detail");
        var filterText = ReadOptionalString(item, "filterText");
        var sortText = ReadOptionalString(item, "sortText");
        var insertText = ReadOptionalString(item, "insertText") ?? label;
        var documentation = item.TryGetProperty("documentation", out var documentationElement)
            ? LspFeatureParsing.ParseMarkup(documentationElement, "Completion documentation")
            : null;
        var textEdit = item.TryGetProperty("textEdit", out var textEditElement) && textEditElement.ValueKind is not JsonValueKind.Null
            ? ParseCompletionTextEdit(textEditElement)
            : null;
        var additionalEdits = item.TryGetProperty("additionalTextEdits", out var additionalElement) && additionalElement.ValueKind == JsonValueKind.Array
            ? additionalElement.EnumerateArray().Select(ParseTextEdit).ToArray()
            : Array.Empty<RocketTextEdit>();

        return new RocketCompletionItem(label, kind, detail, documentation, insertText, filterText, sortText, textEdit, additionalEdits);
    }

    private static RocketCompletionTextEdit ParseCompletionTextEdit(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("newText", out var newTextElement) ||
            newTextElement.ValueKind != JsonValueKind.String)
        {
            throw new LspProtocolException("Completion textEdit is missing newText.");
        }

        var newText = newTextElement.GetString() ?? string.Empty;
        if (element.TryGetProperty("range", out var rangeElement))
        {
            var range = LspFeatureParsing.ParseRange(rangeElement, "Completion textEdit");
            return new RocketCompletionTextEdit(range, range, newText);
        }

        if (element.TryGetProperty("insert", out var insertElement) &&
            element.TryGetProperty("replace", out var replaceElement))
        {
            return new RocketCompletionTextEdit(
                LspFeatureParsing.ParseRange(insertElement, "Completion insert range"),
                LspFeatureParsing.ParseRange(replaceElement, "Completion replace range"),
                newText);
        }

        throw new LspProtocolException("Completion textEdit must provide range or insert/replace ranges.");
    }

    private static RocketTextEdit ParseTextEdit(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("range", out var rangeElement) ||
            !element.TryGetProperty("newText", out var newTextElement) ||
            newTextElement.ValueKind != JsonValueKind.String)
        {
            throw new LspProtocolException("Completion additionalTextEdit is malformed.");
        }

        return new RocketTextEdit(
            LspFeatureParsing.ParseRange(rangeElement, "Completion additionalTextEdit"),
            newTextElement.GetString() ?? string.Empty);
    }

    private static string? ReadOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
