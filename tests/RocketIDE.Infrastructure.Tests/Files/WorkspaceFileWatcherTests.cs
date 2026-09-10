using RocketIDE.Core.Workspaces;
using RocketIDE.Infrastructure.Files;

namespace RocketIDE.Infrastructure.Tests.Files;

[TestClass]
public sealed class WorkspaceFileWatcherTests
{
    [TestMethod]
    public async Task Watcher_ReportsCreateModifyRenameAndDeleteWithoutEventStorms()
    {
        using var temp = new TempDirectory();
        using var watcher = new WorkspaceFileWatcher(temp.Path, TimeSpan.FromMilliseconds(120));
        var batches = new List<WorkspaceChange>();
        var gate = new object();
        watcher.ChangesAvailable += (_, args) =>
        {
            lock (gate)
            {
                batches.AddRange(args.Changes);
            }
        };

        watcher.Start();
        var first = Path.Combine(temp.Path, "first.rocket");
        var renamed = Path.Combine(temp.Path, "renamed.rocket");

        await File.WriteAllTextAsync(first, "fn main() -> Int:\n    return 0\n");
        await WaitUntilAsync(() => ContainsChange(WorkspaceChangeKind.Created, first));

        await File.AppendAllTextAsync(first, "# changed\n");
        await WaitUntilAsync(() => ContainsChange(WorkspaceChangeKind.Changed, first));

        File.Move(first, renamed);
        await WaitUntilAsync(() => ContainsChange(WorkspaceChangeKind.Renamed, renamed));

        File.Delete(renamed);
        await WaitUntilAsync(() => ContainsChange(WorkspaceChangeKind.Deleted, renamed));

        lock (gate)
        {
            Assert.IsTrue(batches.Count(change => change.Kind == WorkspaceChangeKind.Changed &&
                string.Equals(change.Path, first, StringComparison.OrdinalIgnoreCase)) <= 2,
                "Debounce should coalesce duplicate Changed notifications.");
        }

        bool ContainsChange(WorkspaceChangeKind kind, string path)
        {
            lock (gate)
            {
                return batches.Any(change => change.Kind == kind &&
                    string.Equals(change.Path, path, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.IsTrue(condition(), "Expected file-system notifications were not observed before timeout.");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-watch-{Guid.NewGuid():N}");
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
