using RocketIDE.Infrastructure.Settings;

namespace RocketIDE.Infrastructure.Tests.Settings;

[TestClass]
public sealed class EditorPreferencesStoreTests
{
    [TestMethod]
    public async Task MissingFile_LoadsSaneDefaults()
    {
        using var temp = new TempDirectory();
        var store = new EditorPreferencesStore(Path.Combine(temp.Path, "editor.json"));

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(EditorPreferences.DefaultFontSize, loaded.FontSize);
        Assert.IsFalse(loaded.WordWrap);
        Assert.IsFalse(loaded.FormatOnSave);
    }

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsExplicitPreferences()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "editor.json");
        var store = new EditorPreferencesStore(path);
        var expected = new EditorPreferences { FontSize = 19, WordWrap = true, FormatOnSave = true };

        await store.SaveAsync(expected, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(expected, loaded);
    }

    [TestMethod]
    public async Task CorruptOrOutOfRangeSettings_DegradeSafely()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "editor.json");
        var store = new EditorPreferencesStore(path);
        await File.WriteAllTextAsync(path, "{ definitely-not-json");

        var corrupt = await store.LoadAsync(CancellationToken.None);
        Assert.AreEqual(EditorPreferences.Default, corrupt);

        await File.WriteAllTextAsync(path, "{\"fontSize\":999,\"wordWrap\":true}");
        var normalized = await store.LoadAsync(CancellationToken.None);
        Assert.AreEqual(EditorPreferences.MaximumFontSize, normalized.FontSize);
        Assert.IsTrue(normalized.WordWrap);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-editor-prefs-{Guid.NewGuid():N}");
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
