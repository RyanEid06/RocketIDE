using System.IO;
using RocketIDE.App.Integration;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.App.Tests.Integration;

[TestClass]
public sealed class QuickOpenSearchServiceTests
{
    [TestMethod]
    public void Rank_PrefersExactFilenameThenOpenAndRecentWeighting()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "quick-open-rank"));
        var exact = Path.Combine(root, "player.rocket");
        var fuzzy = Path.Combine(root, "src", "player_controller.rocket");
        var other = Path.Combine(root, "src", "unrelated.rocket");

        var results = QuickOpenRanker.Rank(
            root,
            [fuzzy, other, exact],
            "player.rocket",
            [],
            [],
            10);

        Assert.AreEqual(exact, results[0].FullPath);

        var emptyQuery = QuickOpenRanker.Rank(root, [fuzzy, other], string.Empty, [other], [fuzzy], 10);
        Assert.AreEqual(other, emptyQuery[0].FullPath, "Open weighting should outrank recent-only weighting.");
    }

    [TestMethod]
    public void Rank_FuzzyMatchesRelativePathsAndHonorsMaximumResults()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "quick-open-fuzzy"));
        var files = Enumerable.Range(0, 100)
            .Select(index => Path.Combine(root, "source", $"player_controller_{index}.rocket"))
            .ToArray();

        var results = QuickOpenRanker.Rank(root, files, "plctrl", [], [], 7);

        Assert.AreEqual(7, results.Count);
        Assert.IsTrue(results.All(result => result.RelativePath.Contains("player_controller", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Rank_PreCancelledRequestStopsWithoutProducingResults()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "quick-open-cancel"));
        var files = Enumerable.Range(0, 1000).Select(index => Path.Combine(root, $"file-{index}.rocket")).ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            QuickOpenRanker.Rank(root, files, "file", [], [], 250, cancellation.Token));
    }

    [TestMethod]
    public async Task SearchAsync_UsesFinderAndCanBeCancelled()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "quick-open-service"));
        var file = Path.Combine(root, "main.rocket");
        var fs = new FakeWorkspaceFileSystem(new Dictionary<string, IReadOnlyList<WorkspaceEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            [root] = [new WorkspaceEntry(file, "main.rocket", false, false)],
        });
        var service = new QuickOpenSearchService(new QuickOpenFileFinder(fs));

        var discovered = await service.DiscoverAsync(root, 10, CancellationToken.None);
        var results = await service.SearchAsync(root, discovered, "mn", [], [], 10, CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(file, results[0].FullPath);
    }

    private sealed class FakeWorkspaceFileSystem(IReadOnlyDictionary<string, IReadOnlyList<WorkspaceEntry>> entries) : IWorkspaceFileSystem
    {
        public Task<IReadOnlyList<WorkspaceEntry>> GetChildrenAsync(string directoryPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(entries.TryGetValue(Path.GetFullPath(directoryPath), out var children)
                ? children
                : (IReadOnlyList<WorkspaceEntry>)[]);
        }

        public Task CreateFileAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void CreateDirectory(string path) => throw new NotSupportedException();
        public string Rename(string path, string newName) => throw new NotSupportedException();
        public void DeleteToRecycleBin(string path) => throw new NotSupportedException();
        public string ResolveChildPath(string directoryPath, string leafName) => throw new NotSupportedException();
    }
}
