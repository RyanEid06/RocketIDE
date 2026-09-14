using RocketIDE.Core.Commands;
using RocketIDE.Core.Output;
using RocketIDE.Rocket.Projects;
using RocketIDE.Rocket.Tools;

namespace RocketIDE.Rocket.Compiler;

public sealed record RocketCommandOutput(
    string DisplayText,
    ProcessOutputStream Stream,
    RocketMessage? Message = null);

public sealed record RocketCommandExecutionResult(
    RocketCommandKind Kind,
    RocketTarget Target,
    ProcessRunResult ProcessResult);

public sealed record RocketAdvancedCommandExecutionResult(
    RocketAdvancedCommandKind Kind,
    RocketTarget? Target,
    RocketAdvancedCommand Command,
    ProcessRunResult ProcessResult);

public sealed class RocketCommandService
{
    private readonly IProcessRunner _processRunner;
    private readonly IRocketToolLocator _toolLocator;
    private readonly IRocketTargetDiscovery _targetDiscovery;
    private int _running;

    public RocketCommandService(
        IProcessRunner processRunner,
        IRocketToolLocator toolLocator,
        IRocketTargetDiscovery targetDiscovery)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _toolLocator = toolLocator ?? throw new ArgumentNullException(nameof(toolLocator));
        _targetDiscovery = targetDiscovery ?? throw new ArgumentNullException(nameof(targetDiscovery));
    }

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public async Task<RocketCommandExecutionResult> ExecuteAsync(
        RocketCommandKind kind,
        string activePath,
        IReadOnlyList<string>? programArguments,
        IProgress<RocketCommandOutput> output,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activePath);
        ArgumentNullException.ThrowIfNull(output);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InvalidOperationException("A Rocket command is already running. Stop it before starting another command.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = _targetDiscovery.Discover(activePath)
                ?? throw new InvalidOperationException("Open a Rocket source file or rocket.toml target before using a Rocket command.");
            var discovery = await _toolLocator.DiscoverAsync(activePath, cancellationToken).ConfigureAwait(false);
            if (discovery.CompilerPath is null || discovery.CompilerVersion is null)
            {
                var detail = discovery.Problems.FirstOrDefault(problem =>
                    problem.Contains("rocketc", StringComparison.OrdinalIgnoreCase));
                throw new InvalidOperationException(detail ?? "A validated Rocket compiler could not be discovered.");
            }
            var spec = RocketCommandBuilder.Build(discovery.CompilerPath, kind, target, programArguments);
            var progress = new InlineProgress<ProcessOutput>(line =>
            {
                if (spec.UsesStructuredMessages &&
                    line.Stream == ProcessOutputStream.StandardOutput &&
                    RocketMessageParser.TryParse(line.Text, out var message) &&
                    message is not null)
                {
                    output.Report(new RocketCommandOutput(
                        RocketMessageParser.FormatForOutput(message),
                        line.Stream,
                        message));
                    return;
                }

                // Unknown schema, malformed JSON, stderr, and normal program text stay visible.
                output.Report(new RocketCommandOutput(line.Text, line.Stream));
            });

            var result = await _processRunner.RunAsync(spec.Request, progress, cancellationToken).ConfigureAwait(false);
            return new RocketCommandExecutionResult(kind, target, result);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    public void StopActive() => _processRunner.StopActive();

    public async Task<RocketAdvancedCommandExecutionResult> ExecuteAdvancedAsync(
        RocketAdvancedCommandKind kind,
        string discoveryPath,
        RocketAdvancedCommandOptions? options,
        IProgress<RocketCommandOutput> output,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discoveryPath);
        ArgumentNullException.ThrowIfNull(output);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InvalidOperationException("A Rocket command is already running. Stop it before starting another command.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = kind == RocketAdvancedCommandKind.New ? null : _targetDiscovery.Discover(discoveryPath)
                ?? throw new InvalidOperationException("Open a Rocket source file or rocket.toml target before using this command.");
            var discovery = await _toolLocator.DiscoverAsync(discoveryPath, cancellationToken).ConfigureAwait(false);
            if (discovery.CompilerPath is null || discovery.CompilerVersion is null)
            {
                var detail = discovery.Problems.FirstOrDefault(problem =>
                    problem.Contains("rocketc", StringComparison.OrdinalIgnoreCase));
                throw new InvalidOperationException(detail ?? "A validated Rocket compiler could not be discovered.");
            }

            var command = RocketCommandCatalog.Build(discovery.CompilerPath, kind, target, options);
            var progress = new InlineProgress<ProcessOutput>(line =>
                output.Report(new RocketCommandOutput(line.Text, line.Stream)));
            var result = await _processRunner.RunAsync(command.Request, progress, cancellationToken).ConfigureAwait(false);
            return new RocketAdvancedCommandExecutionResult(kind, target, command, result);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
