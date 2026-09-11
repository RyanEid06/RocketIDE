using System.Text;
using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record RocketHover(RocketMarkupContent Contents, LspRange? Range);

public sealed class HoverClient(IRocketLanguageClient client)
{
    public async Task<RocketHover?> RequestAsync(string path, LspPosition position, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(position);
        var response = await client.RequestAsync<JsonElement>(
            "textDocument/hover",
            new { textDocument = new { uri = LspFeatureParsing.PathToUri(path) }, position },
            cancellationToken).ConfigureAwait(false);
        return ParseResponse(response);
    }

    internal static RocketHover? ParseResponse(JsonElement response)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("contents", out var contents))
        {
            throw new LspProtocolException("textDocument/hover response is missing contents.");
        }

        var markup = ParseHoverContents(contents);
        if (markup is null)
        {
            return null;
        }

        LspRange? range = null;
        if (response.TryGetProperty("range", out var rangeElement) && rangeElement.ValueKind is not JsonValueKind.Null)
        {
            range = LspFeatureParsing.ParseRange(rangeElement, "Hover range");
        }
        return new RocketHover(markup, range);
    }

    private static RocketMarkupContent? ParseHoverContents(JsonElement contents)
    {
        var direct = LspFeatureParsing.ParseMarkup(contents, "Hover contents");
        if (direct is not null)
        {
            return direct;
        }

        if (contents.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException("Hover contents has unsupported shape.");
        }

        var builder = new StringBuilder();
        foreach (var entry in contents.EnumerateArray())
        {
            string? text = entry.ValueKind switch
            {
                JsonValueKind.String => entry.GetString(),
                JsonValueKind.Object when entry.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String => value.GetString(),
                _ => null,
            };
            if (text is null)
            {
                continue;
            }
            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine();
            }
            builder.Append(text);
        }

        return builder.Length == 0 ? null : new RocketMarkupContent("plaintext", builder.ToString());
    }
}
