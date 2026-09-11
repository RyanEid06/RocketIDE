using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record RocketParameterInformation(
    string? Label,
    int? LabelStart,
    int? LabelEnd,
    RocketMarkupContent? Documentation);

public sealed record RocketSignatureInformation(
    string Label,
    RocketMarkupContent? Documentation,
    IReadOnlyList<RocketParameterInformation> Parameters,
    int? ActiveParameter);

public sealed record RocketSignatureHelp(
    IReadOnlyList<RocketSignatureInformation> Signatures,
    int ActiveSignature,
    int ActiveParameter);

public sealed class SignatureHelpClient(IRocketLanguageClient client)
{
    public async Task<RocketSignatureHelp?> RequestAsync(
        string path,
        LspPosition position,
        string? triggerCharacter,
        bool isRetrigger,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(position);
        var response = await client.RequestAsync<JsonElement>(
            "textDocument/signatureHelp",
            new
            {
                textDocument = new { uri = LspFeatureParsing.PathToUri(path) },
                position,
                context = new
                {
                    triggerKind = string.IsNullOrEmpty(triggerCharacter) ? 1 : 2,
                    triggerCharacter,
                    isRetrigger,
                },
            },
            cancellationToken).ConfigureAwait(false);
        return ParseResponse(response);
    }

    internal static RocketSignatureHelp? ParseResponse(JsonElement response)
    {
        if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("signatures", out var signaturesElement) ||
            signaturesElement.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException("textDocument/signatureHelp response is missing signatures.");
        }

        var signatures = signaturesElement.EnumerateArray().Select(ParseSignature).ToArray();
        if (signatures.Length == 0)
        {
            return null;
        }

        var activeSignature = ReadNonNegativeInt(response, "activeSignature") ?? 0;
        activeSignature = Math.Clamp(activeSignature, 0, signatures.Length - 1);
        var topLevelActiveParameter = ReadNonNegativeInt(response, "activeParameter") ?? 0;
        var activeParameter = signatures[activeSignature].ActiveParameter ?? topLevelActiveParameter;
        return new RocketSignatureHelp(signatures, activeSignature, Math.Max(0, activeParameter));
    }

    private static RocketSignatureInformation ParseSignature(JsonElement signature)
    {
        if (signature.ValueKind != JsonValueKind.Object ||
            !signature.TryGetProperty("label", out var labelElement) ||
            labelElement.ValueKind != JsonValueKind.String)
        {
            throw new LspProtocolException("Signature information is missing label.");
        }

        var documentation = signature.TryGetProperty("documentation", out var documentationElement)
            ? LspFeatureParsing.ParseMarkup(documentationElement, "Signature documentation")
            : null;
        var parameters = signature.TryGetProperty("parameters", out var parametersElement) && parametersElement.ValueKind == JsonValueKind.Array
            ? parametersElement.EnumerateArray().Select(ParseParameter).ToArray()
            : Array.Empty<RocketParameterInformation>();
        return new RocketSignatureInformation(
            labelElement.GetString() ?? string.Empty,
            documentation,
            parameters,
            ReadNonNegativeInt(signature, "activeParameter"));
    }

    private static RocketParameterInformation ParseParameter(JsonElement parameter)
    {
        if (parameter.ValueKind != JsonValueKind.Object || !parameter.TryGetProperty("label", out var labelElement))
        {
            throw new LspProtocolException("Signature parameter is missing label.");
        }

        string? label = null;
        int? labelStart = null;
        int? labelEnd = null;
        if (labelElement.ValueKind == JsonValueKind.String)
        {
            label = labelElement.GetString();
        }
        else if (labelElement.ValueKind == JsonValueKind.Array)
        {
            var offsets = labelElement.EnumerateArray().ToArray();
            if (offsets.Length != 2 || !offsets[0].TryGetInt32(out var start) || !offsets[1].TryGetInt32(out var end) || start < 0 || end < start)
            {
                throw new LspProtocolException("Signature parameter offset label is invalid.");
            }
            labelStart = start;
            labelEnd = end;
        }
        else
        {
            throw new LspProtocolException("Signature parameter label has unsupported shape.");
        }

        var documentation = parameter.TryGetProperty("documentation", out var documentationElement)
            ? LspFeatureParsing.ParseMarkup(documentationElement, "Signature parameter documentation")
            : null;
        return new RocketParameterInformation(label, labelStart, labelEnd, documentation);
    }

    private static int? ReadNonNegativeInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && number >= 0 ? number : null;
}
