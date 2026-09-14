using RocketIDE.Core.Recovery;
using RocketIDE.Infrastructure.Recovery;

namespace RocketIDE.Infrastructure.Tests.Recovery;

[TestClass]
public sealed class JsonRecoveryStoreTests
{
    [TestMethod]
    public async Task SaveLoadMarksChangedDiskFileAsConflict()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        await File.WriteAllTextAsync(source, "saved");
        var fingerprint = await JsonRecoveryStore.ComputeFingerprintAsync(source, CancellationToken.None);
        var store = new JsonRecoveryStore(Path.Combine(temp.Path, "state", "recovery.json"));
        await store.SaveAsync(new RecoverySet([
            new RecoverySnapshot(source, fingerprint, File.GetLastWriteTimeUtc(source), 1, "unsaved", DateTimeOffset.UtcNow)
        ], DateTimeOffset.UtcNow), CancellationToken.None);

        await File.WriteAllTextAsync(source, "changed on disk");

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.IsTrue(loaded.Snapshots[0].IsConflict);
        Assert.AreEqual("unsaved", loaded.Snapshots[0].Text);
    }

    [TestMethod]
    public async Task ClearRemovesRecoveryState()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "recovery.json");
        var store = new JsonRecoveryStore(path);
        await store.SaveAsync(new RecoverySet([], DateTimeOffset.UtcNow), CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.IsFalse(File.Exists(path));
        Assert.IsFalse((await store.LoadAsync(CancellationToken.None)).HasSnapshots);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-recovery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
