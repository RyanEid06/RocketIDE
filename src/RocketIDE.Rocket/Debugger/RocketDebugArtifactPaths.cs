using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Debugger;

public sealed record RocketDebugArtifactPaths(
    string ExecutablePath,
    string PdbPath,
    string SourceMapPath,
    string SourceRoot,
    string WorkingDirectory)
{
    public static RocketDebugArtifactPaths FromCompilerArtifact(RocketTarget target, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        if (!target.IsExecutable)
        {
            throw new InvalidOperationException($"Debugging is unavailable for Rocket {target.OutputKind} targets.");
        }

        var executable = Path.IsPathFullyQualified(artifactPath)
            ? Path.GetFullPath(artifactPath)
            : Path.GetFullPath(Path.Combine(target.WorkingDirectory, artifactPath));
        if (!string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Rocket debug build did not report an executable artifact: {artifactPath}");
        }

        var directory = Path.GetDirectoryName(executable)
            ?? throw new InvalidOperationException($"Cannot determine debug artifact directory for '{executable}'.");
        var stem = Path.GetFileNameWithoutExtension(executable);
        return new RocketDebugArtifactPaths(
            executable,
            Path.Combine(directory, $"{stem}.pdb"),
            Path.Combine(directory, $"{stem}.rocket.map.json"),
            Path.GetFullPath(target.WorkingDirectory),
            Path.GetFullPath(target.WorkingDirectory));
    }
}
