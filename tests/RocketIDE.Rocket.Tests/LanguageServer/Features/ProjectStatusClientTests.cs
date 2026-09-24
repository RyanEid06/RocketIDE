using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class ProjectStatusClientTests
{
    [TestMethod]
    public async Task RequestAsync_MethodNotFound_IsCleanlyUnsupported()
    {
        var client = new UnsupportedClient();
        var result = await new ProjectStatusClient(client).RequestAsync(CancellationToken.None);
        Assert.IsFalse(result.IsSupported);
        Assert.IsNull(result.Status);
    }

    private sealed class UnsupportedClient : IRocketLanguageClient
    {
        public bool IsInitialized => true;
        public RocketLanguageServerCapabilities Capabilities => RocketLanguageServerCapabilities.None;
        public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived { add { } remove { } }
        public event EventHandler<RocketTransportFaultedEventArgs>? Faulted { add { } remove { } }
        public event EventHandler<string>? LogReceived { add { } remove { } }
        public Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken) =>
            throw new JsonRpcResponseException(-32601, "method not found");
        public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
