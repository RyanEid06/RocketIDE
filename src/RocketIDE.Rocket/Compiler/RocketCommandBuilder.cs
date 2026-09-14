using RocketIDE.Core.Commands;
using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Compiler;

public enum RocketCommandKind
{
    Check,
    Build,
    Run,
    Test,
}

public sealed record RocketCommandSpec(
    RocketCommandKind Kind,
    RocketTarget Target,
    ProcessStartRequest Request,
    bool UsesStructuredMessages);

public static class RocketCommandBuilder
{
    public static RocketCommandSpec Build(
        string compilerPath,
        RocketCommandKind kind,
        RocketTarget target,
        IReadOnlyList<string>? programArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerPath);
        ArgumentNullException.ThrowIfNull(target);

        var compiler = Path.GetFullPath(compilerPath);
        var compilerDirectory = Path.GetDirectoryName(compiler);
        if (string.IsNullOrWhiteSpace(compilerDirectory))
        {
            throw new InvalidOperationException($"Cannot determine compiler directory for '{compiler}'.");
        }

        if (kind == RocketCommandKind.Run && !target.IsExecutable)
        {
            throw new InvalidOperationException($"Run is unavailable for Rocket {target.OutputKind} targets.");
        }

        var input = target.IsStandalone ? target.InputPath : target.WorkingDirectory;
        var command = kind.ToString().ToLowerInvariant();
        var arguments = new List<string> { command, Path.GetFullPath(input) };
        var structured = kind is RocketCommandKind.Check or RocketCommandKind.Build or RocketCommandKind.Test;
        if (structured)
        {
            // Rocket accepts this tooling flag after the target input on check/build/test.
            arguments.Add("--message-format=json");
        }
        else if (programArguments is { Count: > 0 })
        {
            // `--` keeps user program arguments separate from rocketc's own command flags.
            arguments.Add("--");
            arguments.AddRange(programArguments);
        }

        return new RocketCommandSpec(
            kind,
            target,
            new ProcessStartRequest(compiler, arguments, compilerDirectory),
            structured);
    }
}
