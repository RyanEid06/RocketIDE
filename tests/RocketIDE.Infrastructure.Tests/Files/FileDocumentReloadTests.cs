using RocketIDE.Core.Documents;
using RocketIDE.Infrastructure.Files;

namespace RocketIDE.Infrastructure.Tests.Files;

[TestClass]
public sealed class FileDocumentReloadTests
{
    [TestMethod]
    public async Task ReloadAsync_ReplacesCleanBufferWithExternalFileAndKeepsItClean()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        await File.WriteAllTextAsync(path, "old\n");
        var store = new FileDocumentStore();
        var opened = await store.OpenAsync(path, CancellationToken.None);
        await File.WriteAllTextAsync(path, "new\n");

        var reloaded = await store.ReloadAsync(opened.Id, CancellationToken.None);

        Assert.AreEqual("new\n", reloaded.Text);
        Assert.IsFalse(reloaded.IsDirty);
        Assert.IsTrue(reloaded.Version > opened.Version);
    }


    [TestMethod]
    public async Task ReloadAsync_Utf8BomKeepsByteLengthAsEditorTextBytes()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "bom.rocket");
        var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(path, "one", encoding);
        var store = new FileDocumentStore();
        var opened = await store.OpenAsync(path, CancellationToken.None);
        await File.WriteAllTextAsync(path, "two-two", encoding);

        var reloaded = await store.ReloadAsync(opened.Id, CancellationToken.None);

        Assert.AreEqual(System.Text.Encoding.UTF8.GetByteCount("two-two"), reloaded.ByteLength);
    }

    [TestMethod]
    public async Task ReloadAsync_RefusesToOverwriteDirtyBuffer()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        await File.WriteAllTextAsync(path, "old\n");
        var store = new FileDocumentStore();
        var opened = await store.OpenAsync(path, CancellationToken.None);
        store.UpdateText(opened.Id, "unsaved\n");
        await File.WriteAllTextAsync(path, "external\n");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.ReloadAsync(opened.Id, CancellationToken.None));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-reload-{Guid.NewGuid():N}");
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
