using System.IO;
using RocketIDE.App.Integration;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.App.Tests.Integration;

[TestClass]
public sealed class QuickOpenFileFinderTests
{
    [TestMethod]
    public async Task FindAsync_RecursesVisibleWorkspaceEntriesAndHonorsLimit()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "quick-open-root"));
        var src = Path.Combine(root, "src");
        var file1 = Path.Combine(root, "a.rocket");
        var file2 = Path.Combine(src, "b.rocket");
        var fs = new FakeWorkspaceFileSystem(new Dictionary<string, IReadOnlyList<WorkspaceEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            [root] = [new WorkspaceEntry(src, "src", true, true), new WorkspaceEntry(file1, "a.rocket", false, false)],
            [src] = [new WorkspaceEntry(file2, "b.rocket", false, false)],
        });
        var finder = new QuickOpenFileFinder(fs);

        var all = await finder.FindAsync(root, 10, CancellationToken.None);
        var limited = await finder.FindAsync(root, 1, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { file1, file2 }, all.ToArray());
        Assert.AreEqual(1, limited.Count);
    }

    [TestMethod]
    public async Task FindAsync_PreCancelledRequestDoesNotEnumerateWorkspace()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "quick-open-cancelled"));
        var fs = new CountingWorkspaceFileSystem();
        var finder = new QuickOpenFileFinder(fs);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => finder.FindAsync(root, 10, cancellation.Token));
        Assert.AreEqual(0, fs.CallCount);
    }

    private sealed class FakeWorkspaceFileSystem(IReadOnlyDictionary<string, IReadOnlyList<WorkspaceEntry>> entries) : IWorkspaceFileSystem
    {
        public Task<IReadOnlyList<WorkspaceEntry>> GetChildrenAsync(string directoryPath, CancellationToken cancellationToken) =>
            Task.FromResult(entries.TryGetValue(Path.GetFullPath(directoryPath), out var children) ? children : (IReadOnlyList<WorkspaceEntry>)[]);
        public Task CreateFileAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void CreateDirectory(string path) => throw new NotSupportedException();
        public string Rename(string path, string newName) => throw new NotSupportedException();
        public void DeleteToRecycleBin(string path) => throw new NotSupportedException();
        public string ResolveChildPath(string directoryPath, string leafName) => throw new NotSupportedException();
    }
    private sealed class CountingWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public int CallCount { get; private set; }
        public Task<IReadOnlyList<WorkspaceEntry>> GetChildrenAsync(string directoryPath, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<WorkspaceEntry>>([]);
        }
        public Task CreateFileAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void CreateDirectory(string path) => throw new NotSupportedException();
        public string Rename(string path, string newName) => throw new NotSupportedException();
        public void DeleteToRecycleBin(string path) => throw new NotSupportedException();
        public string ResolveChildPath(string directoryPath, string leafName) => throw new NotSupportedException();
    }

}
