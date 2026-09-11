using RocketIDE.Infrastructure.Files;

namespace RocketIDE.Infrastructure.Tests.Files;

[TestClass]
public sealed class WorkspaceFileSystemTests
{
    [TestMethod]
    public async Task GetChildrenAsync_IsLazyFriendlyAndHidesBuildMetadataDirectories()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        Directory.CreateDirectory(Path.Combine(temp.Path, ".git"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "bin"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "out"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "rocket.toml"), "[package]\nname = \"demo\"\n");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "phase18_async.bootstrap.obj"), "generated");

        var entries = await new WorkspaceFileSystem().GetChildrenAsync(temp.Path, CancellationToken.None);

        CollectionAssert.AreEquivalent(
            new[] { "src", "rocket.toml" },
            entries.Select(entry => entry.Name).ToArray());
        Assert.IsTrue(entries.First(entry => entry.Name == "src").IsDirectory);
    }

    [TestMethod]
    public async Task CreateAndRename_OperateOnDiskAndRejectDuplicateNames()
    {
        using var temp = new TempDirectory();
        var service = new WorkspaceFileSystem();
        var file = Path.Combine(temp.Path, "main.rocket");
        await service.CreateFileAsync(file, CancellationToken.None);

        var renamed = service.Rename(file, "app.rocket");

        Assert.IsFalse(File.Exists(file));
        Assert.IsTrue(File.Exists(renamed));
        await Assert.ThrowsExactlyAsync<IOException>(
            () => service.CreateFileAsync(renamed, CancellationToken.None));
    }

    [TestMethod]
    public void ResolveChildPath_RejectsTraversalAndNestedPathComponents()
    {
        using var temp = new TempDirectory();
        var service = new WorkspaceFileSystem();

        Assert.ThrowsExactly<ArgumentException>(() => service.ResolveChildPath(temp.Path, @"..\escape.rocket"));
        Assert.ThrowsExactly<ArgumentException>(() => service.ResolveChildPath(temp.Path, "sub/file.rocket"));
        Assert.ThrowsExactly<ArgumentException>(() => service.ResolveChildPath(temp.Path, ".."));
        Assert.ThrowsExactly<ArgumentException>(() => service.ResolveChildPath(temp.Path, " padded.rocket "));
        Assert.ThrowsExactly<ArgumentException>(() => service.ResolveChildPath(temp.Path, "trailing."));
        Assert.ThrowsExactly<ArgumentException>(() => service.ResolveChildPath(temp.Path, "CON.rocket"));
        Assert.ThrowsExactly<ArgumentException>(() => service.ResolveChildPath(temp.Path, "CON .rocket"));
    }

    [TestMethod]
    public void ResolveChildPath_AcceptsSingleLeafInsideSelectedDirectory()
    {
        using var temp = new TempDirectory();
        var service = new WorkspaceFileSystem();

        var path = service.ResolveChildPath(temp.Path, "main.rocket");

        Assert.AreEqual(Path.Combine(temp.Path, "main.rocket"), path);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-fs-{Guid.NewGuid():N}");
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
