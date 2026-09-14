using System.Text;
using RocketIDE.Core.Documents;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer;

[TestClass]
public sealed class LargeFileSynchronizerTests
{
    [TestMethod]
    public async Task OpenAsync_UsesSharedPolicyAndDoesNotSendOversizedText()
    {
        var client = new RecordingClient();
        var synchronizer = new DocumentSynchronizer(client);
        var text = new string('a', (int)LargeFilePolicy.MaxLspDocumentBytes + 1);

        var state = await synchronizer.OpenAsync("large.rocket", text, 1, CancellationToken.None);

        Assert.AreEqual(LspDocumentSyncState.LargeFileUnsupportedByLsp, state);
        Assert.AreEqual(0, client.Notifications);
    }

    private sealed class RecordingClient : IRocketLanguageClient
    {
        public bool IsInitialized => true;
        public RocketLanguageServerCapabilities Capabilities { get; } = RocketLanguageServerCapabilities.None;
        public int Notifications { get; private set; }
        public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived { add { } remove { } }
        public event EventHandler<RocketTransportFaultedEventArgs>? Faulted { add { } remove { } }
        public event EventHandler<string>? LogReceived { add { } remove { } }
        public Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken) => Task.FromResult(default(TResponse));
        public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            Notifications++;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
