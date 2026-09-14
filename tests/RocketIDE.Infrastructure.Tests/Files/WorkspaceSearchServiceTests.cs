using System.Text;
using RocketIDE.Core.Search;
using RocketIDE.Infrastructure.Files;

namespace RocketIDE.Infrastructure.Tests.Files;

[TestClass]
public sealed class WorkspaceSearchServiceTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "RocketIDE.Tests", $"search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SearchAsync_SkipsTransientDirectoriesAndReportsOneBasedMatches()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "main.rocket"), "let needle = 1\nneedle again\n", new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        await File.WriteAllTextAsync(Path.Combine(_root, ".git", "ignored.rocket"), "needle\n", new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        await File.WriteAllTextAsync(Path.Combine(_root, "bin", "ignored.txt"), "needle\n", new UTF8Encoding(false));
        await File.WriteAllBytesAsync(Path.Combine(_root, "data.bin"), [0x00, 0x01, 0x02]);

        var service = new WorkspaceSearchService();
        var batches = new List<SearchResultBatch>();
        var summary = await service.SearchAsync(
            new SearchQuery(_root, "needle", maxResultsPerFile: 10),
            new ImmediateProgress<SearchResultBatch>(batches.Add),
            CancellationToken.None);

        var matches = batches.SelectMany(batch => batch.Matches).ToArray();
        Assert.AreEqual(2, matches.Length);
        Assert.AreEqual(1, matches[0].Line);
        Assert.AreEqual(5, matches[0].Column);
        Assert.AreEqual(2, matches[1].Line);
        Assert.AreEqual(1, matches[1].Column);
        Assert.AreEqual(1, summary.FilesScanned);
        Assert.IsTrue(summary.FilesSkipped >= 2);
        Assert.IsTrue(batches[^1].IsComplete);
    }

    [TestMethod]
    public async Task SearchAsync_StreamsBoundedBatchesAndHonorsResultLimit()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "many.txt"), string.Join('\n', Enumerable.Repeat("needle", 70)), new UTF8Encoding(false));
        var service = new WorkspaceSearchService();
        var batches = new List<SearchResultBatch>();

        var summary = await service.SearchAsync(
            new SearchQuery(_root, "needle", maxResults: 70, maxResultsPerFile: 100),
            new ImmediateProgress<SearchResultBatch>(batches.Add),
            CancellationToken.None);

        Assert.AreEqual(70, summary.TotalMatches);
        Assert.IsTrue(summary.ReachedResultLimit);
        Assert.IsTrue(batches.Count >= 2);
        Assert.IsTrue(batches.All(batch => batch.Matches.Count <= WorkspaceSearchService.DefaultBatchSize));
    }

    [TestMethod]
    public async Task SearchAsync_CancellationStopsEnumeration()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "main.txt"), string.Join('\n', Enumerable.Repeat("needle", 50)), new UTF8Encoding(false));
        var service = new WorkspaceSearchService();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await service.SearchAsync(
                new SearchQuery(_root, "needle", maxResultsPerFile: 100),
                new ImmediateProgress<SearchResultBatch>(_ => cancellation.Cancel()),
                cancellation.Token));
    }

    private sealed class ImmediateProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [TestMethod]
    public async Task ReplacePreview_RequiresFreshFingerprintsBeforeApplying()
    {
        var path = Path.Combine(_root, "main.rocket");
        await File.WriteAllTextAsync(path, "old old\n", new UTF8Encoding(false));
        var service = new WorkspaceSearchService();
        var preview = await service.CreateReplacePreviewAsync(
            new SearchQuery(_root, "old", maxResultsPerFile: 10),
            "new",
            CancellationToken.None);

        await File.WriteAllTextAsync(path, "changed\n", new UTF8Encoding(false));

        await Assert.ThrowsExactlyAsync<WorkspaceReplaceConflictException>(
            () => service.ApplyReplaceAsync(preview, CancellationToken.None));
        Assert.AreEqual("changed\n", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task ApplyReplaceAsync_UpdatesOnlyPreviewedMatches()
    {
        var path = Path.Combine(_root, "main.rocket");
        await File.WriteAllTextAsync(path, "old old\nkeep\n", new UTF8Encoding(false));
        var service = new WorkspaceSearchService();
        var preview = await service.CreateReplacePreviewAsync(
            new SearchQuery(_root, "old", maxResultsPerFile: 10),
            "new",
            CancellationToken.None);

        var result = await service.ApplyReplaceAsync(preview, CancellationToken.None);

        Assert.AreEqual(1, result.FilesChanged);
        Assert.AreEqual(2, result.MatchesReplaced);
        Assert.AreEqual("new new\nkeep\n", await File.ReadAllTextAsync(path));
    }
}
