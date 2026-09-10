using RocketIDE.Infrastructure.Settings;

namespace RocketIDE.Infrastructure.Tests.Settings;

[TestClass]
public sealed class RecentWorkspaceStoreTests
{
    [TestMethod]
    public async Task SaveThenLoad_NormalizesDeduplicatesAndCapsMostRecentEntries()
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "recent-workspaces.json");
        var store = new RecentWorkspaceStore(settingsPath, maxEntries: 3);
        var first = Path.Combine(temp.Path, "one");
        var second = Path.Combine(temp.Path, "two");
        var third = Path.Combine(temp.Path, "three");
        var fourth = Path.Combine(temp.Path, "four");

        await store.SaveAsync(
            new[] { first, second, first + Path.DirectorySeparatorChar, third, fourth },
            CancellationToken.None);

        var loaded = await store.LoadAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { Path.GetFullPath(first), Path.GetFullPath(second), Path.GetFullPath(third) },
            loaded.ToArray());
    }

    [TestMethod]
    public async Task LoadAsync_MalformedSettingsReturnsEmptyInsteadOfBreakingStartup()
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "recent-workspaces.json");
        await File.WriteAllTextAsync(settingsPath, "{not-json");
        var store = new RecentWorkspaceStore(settingsPath);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(0, loaded.Count);
    }

    [TestMethod]
    public async Task SaveAsync_CreatesParentDirectoryAndLeavesNoTemporaryFile()
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "nested", "recent-workspaces.json");
        var store = new RecentWorkspaceStore(settingsPath);
        var workspace = Path.Combine(temp.Path, "project");

        await store.SaveAsync(new[] { workspace }, CancellationToken.None);

        Assert.IsTrue(File.Exists(settingsPath));
        Assert.AreEqual(0, Directory.EnumerateFiles(Path.GetDirectoryName(settingsPath)!, "*.tmp").Count());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-recent-{Guid.NewGuid():N}");
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
