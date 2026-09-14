using RocketIDE.Core.Recovery;
using RocketIDE.Infrastructure.Recovery;

namespace RocketIDE.Infrastructure.Tests.Recovery;

[TestClass]
public sealed class JsonSessionStoreTests
{
    [TestMethod]
    public async Task SaveThenLoad_RoundTripsSessionState()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "session.json");
        var store = new JsonSessionStore(path);
        var state = new SessionState(
            Path.Combine(temp.Path, "workspace"),
            [Path.Combine(temp.Path, "main.rocket")],
            Path.Combine(temp.Path, "main.rocket"),
            new PanelLayout(true, true, 240, 180, 2),
            new WindowBounds(10, 20, 1280, 800, false),
            false,
            DateTimeOffset.UtcNow);

        await store.SaveAsync(state, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(state.WorkspacePath, loaded.WorkspacePath);
        CollectionAssert.AreEqual(state.OpenDocumentPaths.ToArray(), loaded.OpenDocumentPaths.ToArray());
        Assert.AreEqual(state.ActiveDocumentPath, loaded.ActiveDocumentPath);
        Assert.AreEqual(state.Panels, loaded.Panels);
        Assert.AreEqual(state.Window, loaded.Window);
        Assert.AreEqual(state.CleanShutdown, loaded.CleanShutdown);
    }

    [TestMethod]
    public async Task LoadAsync_MalformedJsonReturnsEmptyState()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "session.json");
        await File.WriteAllTextAsync(path, "{broken");
        var store = new JsonSessionStore(path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(DateTimeOffset.UnixEpoch, loaded.SavedUtc);
        Assert.AreEqual(0, loaded.OpenDocumentPaths.Count);
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
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
