namespace RocketIDE.Core.Diagnostics;

public sealed record RocketDiagnostic(
    string Source,
    string Code,
    string Message,
    DiagnosticSeverity Severity,
    string FilePath,
    SourceRange Range);
