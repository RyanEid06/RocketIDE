using RocketIDE.Core.Commands;
using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Compiler;

public enum RocketAdvancedCommandKind
{
    New,
    Resolve,
    Tree,
    Audit,
    Target,
    Format,
    Coverage,
    Profile,
    Benchmark,
}

public sealed record RocketAdvancedCommandOptions(
    string? DestinationPath = null,
    bool Locked = false,
    bool Offline = false,
    bool Verbose = false,
    bool CheckOnly = false,
    string? OutputPath = null,
    bool AllowNonEmptyDestination = false,
    IReadOnlyList<string>? ExtraArguments = null);

public sealed record RocketAdvancedCommand(
    RocketAdvancedCommandKind Kind,
    ProcessStartRequest Request,
    bool MayExecuteNativeCode,
    bool UsesStructuredMessages,
    string? OutputPath);

public static class RocketCommandCatalog
{
    public static RocketAdvancedCommand Build(
        string compilerPath,
        RocketAdvancedCommandKind kind,
        RocketTarget? target,
        RocketAdvancedCommandOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerPath);
        options ??= new RocketAdvancedCommandOptions();

        var compiler = Path.GetFullPath(compilerPath);
        var compilerDirectory = Path.GetDirectoryName(compiler)
            ?? throw new InvalidOperationException($"Cannot determine compiler directory for '{compiler}'.");
        var arguments = new List<string>();
        string? outputPath = null;

        if (kind == RocketAdvancedCommandKind.New)
        {
            var destination = ValidateNewDestination(options);
            arguments.Add("new");
            arguments.Add(destination);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(target);
            var input = target.IsStandalone ? target.InputPath : target.WorkingDirectory;
            arguments.Add(CommandName(kind));
            arguments.Add(Path.GetFullPath(input));

            if (kind == RocketAdvancedCommandKind.Resolve && options.Locked)
            {
                arguments.Add("--locked");
            }
            if (kind == RocketAdvancedCommandKind.Resolve && options.Offline)
            {
                arguments.Add("--offline");
            }
            if (kind == RocketAdvancedCommandKind.Target && options.Verbose)
            {
                arguments.Add("--verbose");
            }
            if (kind == RocketAdvancedCommandKind.Format && options.CheckOnly)
            {
                arguments.Add("--check");
            }
            if (kind is (RocketAdvancedCommandKind.Coverage or RocketAdvancedCommandKind.Profile or RocketAdvancedCommandKind.Benchmark) &&
                !string.IsNullOrWhiteSpace(options.OutputPath))
            {
                outputPath = Path.GetFullPath(options.OutputPath!);
                arguments.Add("--output");
                arguments.Add(outputPath);
            }
        }

        if (options.ExtraArguments is { Count: > 0 })
        {
            arguments.AddRange(options.ExtraArguments);
        }

        return new RocketAdvancedCommand(
            kind,
            new ProcessStartRequest(compiler, arguments, compilerDirectory),
            kind is RocketAdvancedCommandKind.Coverage or RocketAdvancedCommandKind.Profile or RocketAdvancedCommandKind.Benchmark,
            UsesStructuredMessages: false,
            outputPath);
    }

    private static string ValidateNewDestination(RocketAdvancedCommandOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DestinationPath))
        {
            throw new ArgumentException("A destination directory is required for a new Rocket project.", nameof(options));
        }

        var destination = Path.GetFullPath(options.DestinationPath);
        if (File.Exists(destination))
        {
            throw new InvalidOperationException($"The new project destination is a file: '{destination}'.");
        }

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any() && !options.AllowNonEmptyDestination)
        {
            throw new InvalidOperationException($"The new project destination is not empty: '{destination}'. Explicit confirmation is required.");
        }

        return destination;
    }

    private static string CommandName(RocketAdvancedCommandKind kind) => kind switch
    {
        RocketAdvancedCommandKind.Tree => "tree",
        RocketAdvancedCommandKind.Format => "fmt",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
