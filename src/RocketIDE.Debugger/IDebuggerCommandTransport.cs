namespace RocketIDE.Debugger;

public interface IDebuggerCommandTransport : IAsyncDisposable
{
    event EventHandler<RocketDebugOutputEventArgs>? OutputReceived;

    Task CreateProcessAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken);
    Task<string> ExecuteAsync(string command, CancellationToken cancellationToken);
    // Completes when execution stops, whereas ExecuteAsync("g") can return while running.
    async Task ExecuteRunAsync(string command, CancellationToken cancellationToken) =>
        _ = await ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    Task BreakAsync(int processId, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
