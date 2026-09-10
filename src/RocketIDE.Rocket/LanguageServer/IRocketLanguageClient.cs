namespace RocketIDE.Rocket.LanguageServer;

public interface IRocketLanguageClient : IAsyncDisposable
{
    bool IsInitialized { get; }
    event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived;
    event EventHandler<string>? LogReceived;
    Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken);
    Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken);
    Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
