namespace RocketIDE.Rocket.Projects;

public sealed record RocketTarget(
    string InputPath,
    string WorkingDirectory,
    string? ManifestPath,
    bool IsStandalone,
    string OutputKind = "executable",
    string OutputName = "main")
{
    public bool IsExecutable => string.Equals(OutputKind, "executable", StringComparison.OrdinalIgnoreCase);
}
