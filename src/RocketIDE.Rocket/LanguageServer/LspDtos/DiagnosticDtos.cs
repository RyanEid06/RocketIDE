using System.Text.Json;

namespace RocketIDE.Rocket.LanguageServer.LspDtos;

public sealed record LspPublishDiagnosticsParams(
    string? Uri,
    IReadOnlyList<LspDiagnostic>? Diagnostics,
    int? Version = null);

public sealed record LspDiagnostic(
    LspRange? Range,
    string? Message,
    int? Severity = null,
    JsonElement Code = default,
    string? Source = null);
