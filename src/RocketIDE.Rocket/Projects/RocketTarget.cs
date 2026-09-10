namespace RocketIDE.Rocket.Projects;

public sealed record RocketTarget(
    string InputPath,
    string WorkingDirectory,
    string? ManifestPath,
    bool IsStandalone);
