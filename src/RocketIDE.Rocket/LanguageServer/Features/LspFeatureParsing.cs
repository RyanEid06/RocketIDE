using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record RocketMarkupContent(string Kind, string Value);
public sealed record RocketTextEdit(LspRange Range, string NewText);

internal static class LspFeatureParsing
{
    public static LspRange ParseRange(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("start", out var start) ||
            !element.TryGetProperty("end", out var end))
        {
            throw new LspProtocolException($"{context} is missing a valid range.");
        }

        return new LspRange(ParsePosition(start, context), ParsePosition(end, context));
    }

    public static LspPosition ParsePosition(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("line", out var line) || !line.TryGetInt32(out var lineValue) || lineValue < 0 ||
            !element.TryGetProperty("character", out var character) || !character.TryGetInt32(out var characterValue) || characterValue < 0)
        {
            throw new LspProtocolException($"{context} contains an invalid LSP position.");
        }

        return new LspPosition(lineValue, characterValue);
    }

    public static RocketMarkupContent? ParseMarkup(JsonElement element, string context)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new RocketMarkupContent("plaintext", element.GetString() ?? string.Empty);
        }

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("value", out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var kind = element.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String
            ? kindElement.GetString()
            : "plaintext";
        if (!string.Equals(kind, "markdown", StringComparison.Ordinal) &&
            !string.Equals(kind, "plaintext", StringComparison.Ordinal))
        {
            throw new LspProtocolException($"{context} contains unsupported markup kind '{kind}'.");
        }

        return new RocketMarkupContent(kind ?? "plaintext", value.GetString() ?? string.Empty);
    }

    public static string PathToUri(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }
}
