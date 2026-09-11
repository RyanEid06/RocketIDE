using RocketIDE.Core.Diagnostics;

namespace RocketIDE.Rocket.Diagnostics;

public sealed record RocketDiagnosticPublication(
    long Generation,
    string DocumentUri,
    string FilePath,
    int? Version,
    IReadOnlyList<RocketDiagnostic> Diagnostics);
