namespace RocketIDE.Debugger;

public interface IDebuggerCommandTransport : IAsyncDisposable
{
    event EventHandler<RocketDebugOutputEventArgs>? OutputReceived;

    Task CreateProcessAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken);
    Task<string> ExecuteAsync(string command, CancellationToken cancellationToken);
    Task BreakAsync(int processId, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
