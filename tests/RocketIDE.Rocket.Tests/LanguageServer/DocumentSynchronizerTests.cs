using System.Text;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.Tests.LanguageServer;

[TestClass]
public sealed class DocumentSynchronizerTests
{
    [TestMethod]
    public async Task OpenChangeSaveClose_UsesVersionedIncrementalUtf16Synchronization()
    {
        var client = new RecordingClient();
        var sync = new DocumentSynchronizer(client);
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rocketide-sync.rocket"));

        var opened = await sync.OpenAsync(path, "fn main():\n    return \"🚀\"\n", version: 7, CancellationToken.None);
        var changed = await sync.ChangeAsync(path, "fn main():\n    return \"🚀!\"\n", version: 8, CancellationToken.None);
        await sync.SaveAsync(path, CancellationToken.None);
        await sync.CloseAsync(path, CancellationToken.None);

        Assert.AreEqual(LspDocumentSyncState.Synchronized, opened);
        Assert.AreEqual(LspDocumentSyncState.Synchronized, changed);
        Assert.AreEqual("textDocument/didOpen", client.Notifications[0].Method);
        Assert.AreEqual("textDocument/didChange", client.Notifications[1].Method);
        var change = (DidChangeTextDocumentParams)client.Notifications[1].Parameters!;
        Assert.AreEqual(8, change.TextDocument.Version);
        Assert.AreEqual(1, change.ContentChanges.Count);
        Assert.IsNotNull(change.ContentChanges[0].Range);
        Assert.AreEqual(14, change.ContentChanges[0].Range!.Start.Character);
        Assert.AreEqual(14, change.ContentChanges[0].Range!.End.Character);
        Assert.AreEqual("!", change.ContentChanges[0].Text);
        Assert.AreEqual("textDocument/didSave", client.Notifications[2].Method);
        var save = (DidSaveTextDocumentParams)client.Notifications[2].Parameters!;
        Assert.AreEqual("fn main():\n    return \"🚀!\"\n", save.Text);
        Assert.AreEqual("textDocument/didClose", client.Notifications[3].Method);
    }

    [TestMethod]
    public async Task OpenAsync_RejectsDocumentAbove4MiBBeforeTransport()
    {
        var client = new RecordingClient();
        var sync = new DocumentSynchronizer(client);
        var oversized = new string('a', DocumentSynchronizer.MaxDocumentBytes + 1);

        var state = await sync.OpenAsync("large.rocket", oversized, 1, CancellationToken.None);

        Assert.AreEqual(LspDocumentSyncState.LargeFileUnsupportedByLsp, state);
        Assert.AreEqual(0, client.Notifications.Count);
    }

    [TestMethod]
    public async Task OpenAsync_AllowsDocumentAtExact4MiBUtf8Boundary()
    {
        var client = new RecordingClient();
        var sync = new DocumentSynchronizer(client);
        var exact = new string('a', DocumentSynchronizer.MaxDocumentBytes);
        Assert.AreEqual(DocumentSynchronizer.MaxDocumentBytes, Encoding.UTF8.GetByteCount(exact));

        var state = await sync.OpenAsync("boundary.rocket", exact, 1, CancellationToken.None);

        Assert.AreEqual(LspDocumentSyncState.Synchronized, state);
        Assert.AreEqual(1, client.Notifications.Count);
    }

    [TestMethod]
    public async Task ChangeAsync_DoesNotSplitSurrogatePairWhenComputingIncrementalRange()
    {
        var client = new RecordingClient();
        var sync = new DocumentSynchronizer(client);
        await sync.OpenAsync("surrogate.rocket", "a🚀b", 1, CancellationToken.None);

        await sync.ChangeAsync("surrogate.rocket", "a🚀Xb", 2, CancellationToken.None);

        var change = (DidChangeTextDocumentParams)client.Notifications[1].Parameters!;
        Assert.AreEqual(3, change.ContentChanges[0].Range!.Start.Character);
        Assert.AreEqual(3, change.ContentChanges[0].Range!.End.Character);
        Assert.AreEqual("X", change.ContentChanges[0].Text);
    }

    private sealed class RecordingClient : IRocketLanguageClient
    {
        public bool IsInitialized => true;
        public List<(string Method, object? Parameters)> Notifications { get; } = new();
        public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived { add { } remove { } }
        public event EventHandler<string>? LogReceived { add { } remove { } }
        public Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken) => Task.FromResult(default(TResponse));
        public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            Notifications.Add((method, parameters));
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
