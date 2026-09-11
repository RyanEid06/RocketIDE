using System.IO;
using System.Collections.Concurrent;
using RocketIDE.App.Integration;
using RocketIDE.Infrastructure.Settings;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.Tools;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class RocketSessionCoordinatorTests
{
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

    private sealed class FakeLanguageClient : IRocketLanguageClient
    {
        public bool IsInitialized { get; private set; }
        public int DisposeCount { get; private set; }
        public List<(string Method, object? Parameters)> Notifications { get; } = new();

        public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived
        {
            add { }
            remove { }
        }
        public event EventHandler<RocketTransportFaultedEventArgs>? Faulted;
        public event EventHandler<string>? LogReceived
        {
            add { }
            remove { }
        }

        public Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken)
        {
            IsInitialized = true;
            return Task.CompletedTask;
        }

        public Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken) =>
            Task.FromResult(default(TResponse));

        public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            Notifications.Add((method, parameters));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsInitialized = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            IsInitialized = false;
            return ValueTask.CompletedTask;
        }

        public void RaiseFault(Exception exception) => Faulted?.Invoke(this, new RocketTransportFaultedEventArgs(exception));
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
