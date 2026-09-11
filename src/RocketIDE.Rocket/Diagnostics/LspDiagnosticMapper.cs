using System.Text.Json;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.Diagnostics;

public static class LspDiagnosticMapper
{
    public static RocketDiagnosticPublication Map(JsonElement parameters, long generation)
    {
        LspPublishDiagnosticsParams? publication;
        try
        {
            publication = parameters.Deserialize<LspPublishDiagnosticsParams>(LspJson.Options);
        }
        catch (JsonException exception)
        {
            throw new LspProtocolException("textDocument/publishDiagnostics could not be deserialized.", exception);
        }

        if (publication is null || string.IsNullOrWhiteSpace(publication.Uri) || publication.Diagnostics is null)
        {
            throw new LspProtocolException("textDocument/publishDiagnostics is missing uri or diagnostics.");
        }

        if (!Uri.TryCreate(publication.Uri, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            throw new LspProtocolException($"Rocket diagnostic URI '{publication.Uri}' is not an absolute file URI.");
        }

        var filePath = Path.GetFullPath(uri.LocalPath);
        var diagnostics = new RocketDiagnostic[publication.Diagnostics.Count];
        for (var index = 0; index < publication.Diagnostics.Count; index++)
        {
            diagnostics[index] = MapDiagnostic(publication.Diagnostics[index], filePath);
        }

        return new RocketDiagnosticPublication(generation, publication.Uri, filePath, publication.Version, diagnostics);
    }

    private static RocketDiagnostic MapDiagnostic(LspDiagnostic diagnostic, string filePath)
    {
        if (diagnostic.Range is null || diagnostic.Range.Start is null || diagnostic.Range.End is null || diagnostic.Message is null)
        {
            throw new LspProtocolException("Rocket LSP diagnostic is missing range or message.");
        }

        ValidatePosition(diagnostic.Range.Start, "start");
        ValidatePosition(diagnostic.Range.End, "end");
        if (diagnostic.Range.End.Line < diagnostic.Range.Start.Line ||
            (diagnostic.Range.End.Line == diagnostic.Range.Start.Line && diagnostic.Range.End.Character < diagnostic.Range.Start.Character))
        {
            throw new LspProtocolException("Rocket LSP diagnostic range end precedes its start.");
        }

        var source = string.IsNullOrWhiteSpace(diagnostic.Source) ? "rocketc" : diagnostic.Source;
        return new RocketDiagnostic(
            source,
            ReadCode(diagnostic.Code),
            diagnostic.Message,
            MapSeverity(diagnostic.Severity),
            filePath,
            new SourceRange(
                diagnostic.Range.Start.Line,
                diagnostic.Range.Start.Character,
                diagnostic.Range.End.Line,
                diagnostic.Range.End.Character));
    }

    private static DiagnosticSeverity MapSeverity(int? severity) => severity switch
    {
        1 => DiagnosticSeverity.Error,
        2 => DiagnosticSeverity.Warning,
        3 => DiagnosticSeverity.Information,
        4 => DiagnosticSeverity.Hint,
        _ => DiagnosticSeverity.Information,
    };

    private static string ReadCode(JsonElement code) => code.ValueKind switch
    {
        JsonValueKind.String => code.GetString() ?? string.Empty,
        JsonValueKind.Number => code.GetRawText(),
        _ => string.Empty,
    };

    private static void ValidatePosition(LspPosition position, string name)
    {
        if (position.Line < 0 || position.Character < 0)
        {
            throw new LspProtocolException($"Rocket LSP diagnostic {name} position is negative.");
        }
    }
}
