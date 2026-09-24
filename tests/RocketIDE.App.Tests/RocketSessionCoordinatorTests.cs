using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;
using RocketIDE.Rocket.Diagnostics;
using RocketIDE.App.Integration;
using RocketIDE.Infrastructure.Settings;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;
using RocketIDE.Rocket.Tools;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class RocketSessionCoordinatorTests
{
    [TestMethod]
    public async Task Shutdown_HungFakeLspCanBeForceStoppedWithinApplicationDeadline()
    {
        using var temp = new TempDirectory();
        var fakeClient = new FakeLanguageClient { HangOnStop = true };
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"), Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0", "rocket-lsp 1.0.0", []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);
        await coordinator.EnsureAsync(temp.Path, temp.Path, CancellationToken.None);
        using var lifetime = new ApplicationLifetimeCoordinator(
            TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200));
        lifetime.BeginShutdown();

        Assert.IsFalse(await lifetime.RunGracefulAsync("LSP", coordinator.ShutdownAsync));
        coordinator.ForceStopOwnedProcessTree();
        Assert.AreEqual(1, fakeClient.ForcedKillCount);
    }

    [TestMethod]
    public async Task RestartAsync_HungFakeLspStopHonorsDeadlineAndKillsOwnedTree()
    {
        using var temp = new TempDirectory();
        var fakeClient = new FakeLanguageClient { HangOnStop = true };
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"), Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0", "rocket-lsp 1.0.0", []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);
        await coordinator.EnsureAsync(temp.Path, temp.Path, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                coordinator.RestartAsync(temp.Path, temp.Path, cancellation.Token).WaitAsync(TimeSpan.FromMilliseconds(500)));
            Assert.AreEqual(1, fakeClient.ForcedKillCount);
        }
        finally
        {
            if (fakeClient.ForcedKillCount == 0) fakeClient.KillOwnedProcessTree();
        }
    }

    [TestMethod]
    public async Task EnsureAndOpenDocumentAsync_SerializesImmediateChangeAndSaveBehindStartup()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeClient = new FakeLanguageClient { StartGate = startup.Task };
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"), Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0", "rocket-lsp 1.0.0", []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);

        var opened = coordinator.EnsureAndOpenDocumentAsync(
            new RocketSessionDocument(source, "old", 1, "main.rocket"), source, temp.Path, CancellationToken.None);
        var changed = coordinator.ChangeDocumentAsync(
            new RocketSessionDocument(source, "new", 2, "main.rocket"), CancellationToken.None);
        var saved = coordinator.SaveDocumentAsync(
            new RocketSessionDocument(source, "new", 2, "main.rocket"), CancellationToken.None);
        startup.TrySetResult();
        await Task.WhenAll(opened, changed, saved);

        CollectionAssert.AreEqual(
            new[] { "textDocument/didOpen", "textDocument/didChange", "textDocument/didSave" },
            fakeClient.Notifications.Select(notification => notification.Method).ToArray());
    }

    [TestMethod]
    public async Task EnsureAsync_StartsClientAndSynchronizesAlreadyOpenDocuments()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        var fakeClient = new FakeLanguageClient();
        var statuses = new ConcurrentQueue<string>();
        var output = new ConcurrentQueue<string>();
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"),
            Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0",
            "rocket-lsp 1.0.0",
            []);
        await using var coordinator = new RocketSessionCoordinator(
            _ => Task.FromResult(RocketToolSettings.Automatic),
            _ => new FakeLocator(discovery),
            () => fakeClient,
            () => [new RocketSessionDocument(source, "fn main() -> Int:\n    return 0\n", 0, "main.rocket")],
            _ => { },
            statuses.Enqueue,
            output.Enqueue,
            () => { });

        await coordinator.EnsureAsync(source, temp.Path, CancellationToken.None);

        Assert.IsTrue(fakeClient.IsInitialized);
        CollectionAssert.Contains(fakeClient.Notifications.Select(item => item.Method).ToArray(), "textDocument/didOpen");
        Assert.IsTrue(statuses.Any(status => status.Contains("online", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(output.Any(line => line.Contains("rocket-lsp initialized", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task EnsureAsync_CapturesOpenDocumentsBeforeAsynchronousStartup()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        var settingsReady = new TaskCompletionSource<RocketToolSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerThread = -1;
        var callerThread = Environment.CurrentManagedThreadId;
        var fakeClient = new FakeLanguageClient();
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"),
            Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0",
            "rocket-lsp 1.0.0",
            []);
        await using var coordinator = new RocketSessionCoordinator(
            _ => settingsReady.Task,
            _ => new FakeLocator(discovery),
            () => fakeClient,
            () =>
            {
                providerThread = Environment.CurrentManagedThreadId;
                return [new RocketSessionDocument(source, "fn main() -> Int:\n    return 0\n", 0, "main.rocket")];
            },
            _ => { },
            _ => { },
            _ => { },
            () => { });

        var ensure = coordinator.EnsureAsync(source, temp.Path, CancellationToken.None);

        Assert.AreEqual(callerThread, providerThread, "UI-owned document snapshots must be captured before the coordinator crosses an async boundary.");
        settingsReady.TrySetResult(RocketToolSettings.Automatic);
        await ensure;
    }

    [TestMethod]
    public async Task ClientFault_MovesSessionOfflineAndDisposesFaultedClient()
    {
        using var temp = new TempDirectory();
        var fakeClient = new FakeLanguageClient();
        var statuses = new ConcurrentQueue<string>();
        var output = new ConcurrentQueue<string>();
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"),
            Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0",
            "rocket-lsp 1.0.0",
            []);
        await using var coordinator = new RocketSessionCoordinator(
            _ => Task.FromResult(RocketToolSettings.Automatic),
            _ => new FakeLocator(discovery),
            () => fakeClient,
            () => [],
            _ => { },
            statuses.Enqueue,
            output.Enqueue,
            () => { });
        var fault = new TaskCompletionSource<RocketTransportFaultedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Faulted += (_, args) => fault.TrySetResult(args);
        await coordinator.EnsureAsync(temp.Path, temp.Path, CancellationToken.None);

        fakeClient.RaiseFault(new IOException("server died"));
        var observedFault = await fault.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => fakeClient.DisposeCount > 0);

        Assert.AreEqual("server died", observedFault.Exception.Message);
        Assert.IsTrue(statuses.Any(status => status == "LSP: offline"));
        Assert.IsTrue(output.Any(line => line.Contains("server died", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PublishDiagnostics_IsMappedWithCurrentSessionGenerationAndObserverFailuresAreIsolated()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        var fakeClient = new FakeLanguageClient();
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"),
            Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0",
            "rocket-lsp 1.0.0",
            []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);
        var sessions = new List<RocketDiagnosticSessionChangedEventArgs>();
        var publications = new List<RocketDiagnosticPublication>();
        coordinator.DiagnosticSessionChanged += (_, args) => sessions.Add(args);
        coordinator.DiagnosticsPublished += (_, _) => throw new InvalidOperationException("broken UI observer");
        coordinator.DiagnosticsPublished += (_, args) => publications.Add(args.Publication);

        await coordinator.EnsureAsync(source, temp.Path, CancellationToken.None);
        fakeClient.RaiseNotification("textDocument/publishDiagnostics", $$"""
        {
          "uri": {{JsonSerializer.Serialize(new Uri(source).AbsoluteUri)}},
          "version": 0,
          "diagnostics": [
            {
              "range": { "start": {"line":0,"character":1}, "end": {"line":0,"character":3} },
              "severity": 1,
              "code": "R2001",
              "source": "rocketc",
              "message": "bad"
            }
          ]
        }
        """);

        Assert.IsTrue(sessions.Count > 0);
        Assert.IsTrue(sessions[^1].IsOnline);
        Assert.AreEqual(1, publications.Count);
        Assert.AreEqual(sessions[^1].Generation, publications[0].Generation);
        Assert.AreEqual("R2001", publications[0].Diagnostics.Single().Code);
    }

    [TestMethod]
    public async Task RestartAndFault_AdvanceDiagnosticGenerationAndInvalidateOldPublications()
    {
        using var temp = new TempDirectory();
        var first = new FakeLanguageClient();
        var second = new FakeLanguageClient();
        var clients = new Queue<FakeLanguageClient>([first, second]);
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"),
            Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0",
            "rocket-lsp 1.0.0",
            []);
        await using var coordinator = new RocketSessionCoordinator(
            _ => Task.FromResult(RocketToolSettings.Automatic),
            _ => new FakeLocator(discovery),
            () => clients.Dequeue(),
            () => [],
            _ => { },
            _ => { },
            _ => { },
            () => { });
        var sessions = new List<RocketDiagnosticSessionChangedEventArgs>();
        coordinator.DiagnosticSessionChanged += (_, args) => sessions.Add(args);

        await coordinator.EnsureAsync(temp.Path, temp.Path, CancellationToken.None);
        var firstOnline = sessions.Last(item => item.IsOnline);
        await coordinator.RestartAsync(temp.Path, temp.Path, CancellationToken.None);
        var secondOnline = sessions.Last(item => item.IsOnline);

        Assert.IsTrue(secondOnline.Generation > firstOnline.Generation);
        second.RaiseFault(new IOException("server died"));
        await WaitUntilAsync(() => sessions.Count > 0 && !sessions[^1].IsOnline && sessions[^1].Generation > secondOnline.Generation);
    }

    [TestMethod]
    public async Task FaultedSession_IgnoresLateNotificationsFromDeadClient()
    {
        using var temp = new TempDirectory();
        var fakeClient = new FakeLanguageClient();
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"),
            Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0",
            "rocket-lsp 1.0.0",
            []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);
        var forwarded = 0;
        coordinator.NotificationReceived += (_, _) => forwarded++;
        await coordinator.EnsureAsync(temp.Path, temp.Path, CancellationToken.None);

        fakeClient.RaiseFault(new IOException("server died"));
        fakeClient.RaiseNotification("rocket/analysisStatus", "{\"files\":1}");

        Assert.AreEqual(0, forwarded);
    }

    [TestMethod]
    public async Task FeatureRequest_UsesRealLspOnlyForSynchronizedSupportedDocument()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        var fakeClient = new FakeLanguageClient
        {
            Capabilities = new RocketLanguageServerCapabilities(
                true, ["."], false, false, [], [], false, false, SemanticTokenLegend.Empty),
            RequestHandler = (method, _) => method == "textDocument/completion"
                ? JsonDocument.Parse("""[{ "label": "launch", "kind": 3 }]""").RootElement.Clone()
                : null,
        };
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"),
            Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0",
            "rocket-lsp 1.0.0",
            []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);
        await coordinator.EnsureAsync(source, temp.Path, CancellationToken.None);
        await coordinator.OpenDocumentAsync(
            new RocketSessionDocument(source, "fn main() -> Int:\n    return 0\n", 0, "main.rocket"),
            CancellationToken.None);

        var result = await coordinator.RequestCompletionAsync(
            source,
            new LspPosition(0, 2),
            null,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("launch", result.Items.Single().Label);
        CollectionAssert.Contains(fakeClient.Requests.Select(request => request.Method).ToArray(), "textDocument/completion");
    }

    [TestMethod]
    public async Task FeatureRequest_IsSuppressedForLargeUnsynchronizedDocument()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "large.rocket");
        var fakeClient = new FakeLanguageClient
        {
            Capabilities = new RocketLanguageServerCapabilities(
                true, ["."], false, false, [], [], false, false, SemanticTokenLegend.Empty),
        };
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"),
            Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0",
            "rocket-lsp 1.0.0",
            []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);
        await coordinator.EnsureAsync(source, temp.Path, CancellationToken.None);
        await coordinator.OpenDocumentAsync(
            new RocketSessionDocument(source, new string('x', (4 * 1024 * 1024) + 1), 0, "large.rocket"),
            CancellationToken.None);

        var result = await coordinator.RequestCompletionAsync(
            source,
            new LspPosition(0, 0),
            null,
            CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsFalse(fakeClient.Requests.Any(request => request.Method == "textDocument/completion"));
    }

    [TestMethod]
    public async Task DocumentSynchronization_ReportsLargeFileSupportState()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "large.rocket");
        var fakeClient = new FakeLanguageClient();
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"),
            Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0",
            "rocket-lsp 1.0.0",
            []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);
        var states = new List<RocketDocumentSyncStateChangedEventArgs>();
        coordinator.DocumentSyncStateChanged += (_, args) => states.Add(args);
        await coordinator.EnsureAsync(source, temp.Path, CancellationToken.None);

        var text = new string('x', (4 * 1024 * 1024) + 1);
        await coordinator.OpenDocumentAsync(new RocketSessionDocument(source, text, 9, "large.rocket"), CancellationToken.None);

        var state = states.Last();
        Assert.AreEqual(source, state.Path);
        Assert.AreEqual(9, state.Version);
        Assert.AreEqual(LspDocumentSyncState.LargeFileUnsupportedByLsp, state.State);
    }


    [TestMethod]
    public async Task Wp09FeatureRequests_UseAdvertisedCapabilitiesAndSynchronizedDocument()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        var target = Path.Combine(temp.Path, "lib.rocket");
        var fakeClient = new FakeLanguageClient
        {
            Capabilities = RocketLanguageServerCapabilities.None with
            {
                SupportsDefinition = true,
                SupportsReferences = true,
                SupportsRename = true,
                SupportsPrepareRename = true,
                SupportsCodeActions = true,
                SupportsDocumentFormatting = true,
            },
            RequestHandler = (method, _) => method switch
            {
                "textDocument/definition" => JsonDocument.Parse($$"""{ "uri": {{JsonSerializer.Serialize(new Uri(target).AbsoluteUri)}}, "range": { "start": {"line":0,"character":0}, "end": {"line":0,"character":1} } }""").RootElement.Clone(),
                "textDocument/references" => JsonDocument.Parse("[]").RootElement.Clone(),
                "textDocument/prepareRename" => JsonDocument.Parse("""{ "range": { "start": {"line":0,"character":0}, "end": {"line":0,"character":4} }, "placeholder": "name" }""").RootElement.Clone(),
                "textDocument/rename" => JsonDocument.Parse("""{ "changes": {} }""").RootElement.Clone(),
                "textDocument/codeAction" => JsonDocument.Parse("""
                [
                  { "title": "Fix name", "kind": "quickfix" },
                  { "title": "Format Rocket document", "kind": "source.format" }
                ]
                """).RootElement.Clone(),
                "textDocument/formatting" => JsonDocument.Parse("[]").RootElement.Clone(),
                _ => JsonDocument.Parse("null").RootElement.Clone(),
            },
        };
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"), Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0", "rocket-lsp 1.0.0", []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);
        await coordinator.EnsureAsync(source, temp.Path, CancellationToken.None);
        await coordinator.OpenDocumentAsync(new RocketSessionDocument(source, "name", 0, "main.rocket"), CancellationToken.None);

        var position = new LspPosition(0, 1);
        var range = new LspRange(new LspPosition(0, 0), new LspPosition(0, 4));
        var definitions = await coordinator.RequestDefinitionAsync(source, position, CancellationToken.None);
        var references = await coordinator.RequestReferencesAsync(source, position, CancellationToken.None);
        var prepared = await coordinator.PrepareRenameAsync(source, position, CancellationToken.None);
        var rename = await coordinator.RequestRenameAsync(source, position, "other", CancellationToken.None);
        var actions = await coordinator.RequestCodeActionsAsync(
            source,
            range,
            [new RocketCodeActionDiagnostic(
                range, 1, "R4002", "rocket-lsp", "undefined name 'nmae'",
                JsonSerializer.SerializeToElement(new { replacement = "name", token = 23 }))],
            CancellationToken.None);
        var formatting = await coordinator.RequestFormattingAsync(source, 4, true, CancellationToken.None);

        Assert.IsNotNull(definitions);
        Assert.AreEqual(target, definitions.Single().Path);
        Assert.IsNotNull(references);
        Assert.AreEqual("name", prepared?.Placeholder);
        Assert.IsNotNull(rename);
        Assert.IsNotNull(actions);
        Assert.AreEqual(1, actions.Count);
        Assert.AreEqual("Fix name", actions[0].Title);
        Assert.IsNotNull(formatting);
        var methods = fakeClient.Requests.Select(item => item.Method).ToArray();
        CollectionAssert.Contains(methods, "textDocument/definition");
        CollectionAssert.Contains(methods, "textDocument/references");
        CollectionAssert.Contains(methods, "textDocument/prepareRename");
        CollectionAssert.Contains(methods, "textDocument/rename");
        CollectionAssert.Contains(methods, "textDocument/codeAction");
        CollectionAssert.Contains(methods, "textDocument/formatting");

        var codeActionRequest = fakeClient.Requests.Single(item => item.Method == "textDocument/codeAction");
        var codeActionParams = JsonSerializer.SerializeToElement(codeActionRequest.Parameters, LspJson.Options);
        var codeActionContext = codeActionParams.GetProperty("context");
        var forwardedDiagnostic = codeActionContext.GetProperty("diagnostics").EnumerateArray().Single();
        Assert.AreEqual("R4002", forwardedDiagnostic.GetProperty("code").GetString());
        Assert.AreEqual("name", forwardedDiagnostic.GetProperty("data").GetProperty("replacement").GetString());
        Assert.AreEqual(23, forwardedDiagnostic.GetProperty("data").GetProperty("token").GetInt32());
        CollectionAssert.AreEqual(new[] { "quickfix" }, codeActionContext.GetProperty("only").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [TestMethod]
    public async Task PrepareRename_IsSuppressedUnlessServerAdvertisesPrepareProvider()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        var fakeClient = new FakeLanguageClient
        {
            Capabilities = RocketLanguageServerCapabilities.None with { SupportsRename = true, SupportsPrepareRename = false },
        };
        var discovery = new RocketToolDiscoveryResult(
            Path.Combine(temp.Path, "rocketc.exe"), Path.Combine(temp.Path, "rocket-lsp.exe"),
            "rocketc 2.1.0", "rocket-lsp 1.0.0", []);
        await using var coordinator = CreateCoordinator(fakeClient, discovery);
        await coordinator.EnsureAsync(source, temp.Path, CancellationToken.None);
        await coordinator.OpenDocumentAsync(new RocketSessionDocument(source, "name", 0, "main.rocket"), CancellationToken.None);

        var result = await coordinator.PrepareRenameAsync(source, new LspPosition(0, 1), CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsFalse(fakeClient.Requests.Any(item => item.Method == "textDocument/prepareRename"));
    }

    private static RocketSessionCoordinator CreateCoordinator(FakeLanguageClient client, RocketToolDiscoveryResult discovery) =>
        new(
            _ => Task.FromResult(RocketToolSettings.Automatic),
            _ => new FakeLocator(discovery),
            () => client,
            () => [],
            _ => { },
            _ => { },
            _ => { },
            () => { });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition());
    }

    private sealed class FakeLocator(RocketToolDiscoveryResult result) : IRocketToolLocator
    {
        public Task<RocketToolchain?> LocateAsync(string? activePath, CancellationToken cancellationToken) =>
            Task.FromResult(result.Toolchain);

        public Task<RocketToolDiscoveryResult> DiscoverAsync(string? activePath, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class FakeLanguageClient : IRocketLanguageClient, IOwnedProcessTree
    {
        public bool IsInitialized { get; private set; }
        public RocketLanguageServerCapabilities Capabilities { get; set; } = RocketLanguageServerCapabilities.None;
        public int DisposeCount { get; private set; }
        public int ForcedKillCount { get; private set; }
        public bool HangOnStop { get; init; }
        public Task? StartGate { get; init; }
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<string, object?, object?>? RequestHandler { get; init; }
        public List<(string Method, object? Parameters)> Notifications { get; } = new();
        public List<(string Method, object? Parameters)> Requests { get; } = new();

        public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived;
        public event EventHandler<RocketTransportFaultedEventArgs>? Faulted;
        public event EventHandler<string>? LogReceived
        {
            add { }
            remove { }
        }

        public async Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken)
        {
            if (StartGate is not null) await StartGate;
            IsInitialized = true;
        }

        public Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken)
        {
            Requests.Add((method, parameters));
            var response = RequestHandler?.Invoke(method, parameters);
            return Task.FromResult(response is TResponse typed ? typed : default);
        }

        public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            Notifications.Add((method, parameters));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsInitialized = false;
            return HangOnStop ? _stopped.Task : Task.CompletedTask;
        }

        public void KillOwnedProcessTree()
        {
            ForcedKillCount++;
            _stopped.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            IsInitialized = false;
            return ValueTask.CompletedTask;
        }

        public void RaiseFault(Exception exception) => Faulted?.Invoke(this, new RocketTransportFaultedEventArgs(exception));

        public void RaiseNotification(string method, string parametersJson)
        {
            using var document = JsonDocument.Parse(parametersJson);
            NotificationReceived?.Invoke(this, new RocketServerNotificationEventArgs(method, document.RootElement.Clone()));
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-session-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
