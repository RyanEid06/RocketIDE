using System.Text.Json;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record SemanticTokenLegend(IReadOnlyList<string> TokenTypes, IReadOnlyList<string> TokenModifiers)
{
    public static SemanticTokenLegend Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());
}

public sealed record RocketLanguageServerCapabilities(
    bool SupportsCompletion,
    IReadOnlyList<string> CompletionTriggerCharacters,
    bool SupportsHover,
    bool SupportsSignatureHelp,
    IReadOnlyList<string> SignatureTriggerCharacters,
    IReadOnlyList<string> SignatureRetriggerCharacters,
    bool SupportsSemanticTokens,
    bool SupportsSemanticTokenDelta,
    SemanticTokenLegend SemanticTokenLegend)
{
    public static RocketLanguageServerCapabilities None { get; } = new(
        false,
        Array.Empty<string>(),
        false,
        false,
        Array.Empty<string>(),
        Array.Empty<string>(),
        false,
        false,
        SemanticTokenLegend.Empty);

    public static RocketLanguageServerCapabilities Parse(JsonElement capabilities)
    {
        if (capabilities.ValueKind != JsonValueKind.Object)
        {
            return None;
        }

        var supportsCompletion = IsEnabled(capabilities, "completionProvider", out var completionProvider);
        var completionTriggers = supportsCompletion
            ? ReadStringArray(completionProvider, "triggerCharacters")
            : Array.Empty<string>();

        var supportsHover = IsEnabled(capabilities, "hoverProvider", out _);

        var supportsSignature = IsEnabled(capabilities, "signatureHelpProvider", out var signatureProvider);
        var signatureTriggers = supportsSignature
            ? ReadStringArray(signatureProvider, "triggerCharacters")
            : Array.Empty<string>();
        var signatureRetriggers = supportsSignature
            ? ReadStringArray(signatureProvider, "retriggerCharacters")
            : Array.Empty<string>();

        var supportsSemantic = false;
        var supportsDelta = false;
        var legend = SemanticTokenLegend.Empty;
        if (IsEnabled(capabilities, "semanticTokensProvider", out var semanticProvider) &&
            semanticProvider.ValueKind == JsonValueKind.Object &&
            semanticProvider.TryGetProperty("legend", out var legendElement) &&
            legendElement.ValueKind == JsonValueKind.Object)
        {
            var tokenTypes = ReadStringArray(legendElement, "tokenTypes");
            var tokenModifiers = ReadStringArray(legendElement, "tokenModifiers");
            legend = new SemanticTokenLegend(tokenTypes, tokenModifiers);

            if (semanticProvider.TryGetProperty("full", out var full))
            {
                supportsSemantic = full.ValueKind == JsonValueKind.True || full.ValueKind == JsonValueKind.Object;
                supportsDelta = full.ValueKind == JsonValueKind.Object &&
                    full.TryGetProperty("delta", out var delta) &&
                    delta.ValueKind == JsonValueKind.True;
            }
        }

        return new RocketLanguageServerCapabilities(
            supportsCompletion,
            completionTriggers,
            supportsHover,
            supportsSignature,
            signatureTriggers,
            signatureRetriggers,
            supportsSemantic,
            supportsDelta,
            legend);
    }

    private static bool IsEnabled(JsonElement parent, string propertyName, out JsonElement value)
    {
        if (!parent.TryGetProperty(propertyName, out value))
        {
            return false;
        }

        return value.ValueKind is not (JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined);
    }

    private static string[] ReadStringArray(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
            .Select(item => item.GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
