using RocketIDE.Infrastructure.Settings;

namespace RocketIDE.Infrastructure.Tests.Settings;

[TestClass]
public sealed class RocketToolSettingsStoreTests
{
    [TestMethod]
    public async Task SaveThenLoad_PersistsOnlyUserToolOverrides()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "rocket-tools.json");
        var store = new RocketToolSettingsStore(path);
        var expected = new RocketToolSettings("C:\\sdk\\rocketc.exe", "D:\\rocket\\rocket-lsp.exe", ["E:\\trusted\\Rocket"]);

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(expected.CompilerPath, actual.CompilerPath);
        Assert.AreEqual(expected.LanguageServerPath, actual.LanguageServerPath);
        CollectionAssert.AreEqual(expected.TrustedCheckoutRoots ?? [], actual.TrustedCheckoutRoots ?? []);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public async Task ResetAsync_RemovesOverridesWithoutLeavingMachinePathsBehind()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "rocket-tools.json");
        var store = new RocketToolSettingsStore(path);
        await store.SaveAsync(new RocketToolSettings("C:\\local\\rocketc.exe", "C:\\local\\rocket-lsp.exe"), CancellationToken.None);

        await store.ResetAsync(CancellationToken.None);
        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.IsTrue(actual.IsAutomatic);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task LoadAsync_MalformedJsonFallsBackToAutomaticDiscovery()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "rocket-tools.json");
        await File.WriteAllTextAsync(path, "{broken-json");
        var store = new RocketToolSettingsStore(path);

        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.IsTrue(actual.IsAutomatic);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-tool-settings-{Guid.NewGuid():N}");
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
