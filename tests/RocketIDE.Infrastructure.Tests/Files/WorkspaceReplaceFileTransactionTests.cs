using System.Text;
using RocketIDE.Core.Search;
using RocketIDE.Infrastructure.Files;

namespace RocketIDE.Infrastructure.Tests.Files;

[TestClass]
public sealed class WorkspaceReplaceFileTransactionTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "RocketIDE.Tests", $"replace-transaction-{Guid.NewGuid():N}");
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
    public async Task RollbackAsync_RestoresOriginalBytesAfterCommit()
    {
        var path = Path.Combine(_root, "main.rocket");
        await File.WriteAllTextAsync(path, "old old\n", new UTF8Encoding(false));
        var service = new WorkspaceSearchService();
        var preview = await service.CreateReplacePreviewAsync(
            new SearchQuery(_root, "old", maxResultsPerFile: 10),
            "new",
            CancellationToken.None);

        var transaction = await WorkspaceReplaceFileTransaction.PrepareAsync(preview, CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        Assert.AreEqual("new new\n", await File.ReadAllTextAsync(path));

        await transaction.RollbackAsync(CancellationToken.None);

        Assert.AreEqual("old old\n", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task PrepareAsync_RejectsStalePreviewBeforeWritingAnything()
    {
        var firstPath = Path.Combine(_root, "a.rocket");
        var secondPath = Path.Combine(_root, "b.rocket");
        await File.WriteAllTextAsync(firstPath, "old\n", new UTF8Encoding(false));
        await File.WriteAllTextAsync(secondPath, "old\n", new UTF8Encoding(false));
        var service = new WorkspaceSearchService();
        var preview = await service.CreateReplacePreviewAsync(
            new SearchQuery(_root, "old", maxResultsPerFile: 10),
            "new",
            CancellationToken.None);
        await File.WriteAllTextAsync(secondPath, "changed\n", new UTF8Encoding(false));

        await Assert.ThrowsExactlyAsync<WorkspaceReplaceConflictException>(
            () => WorkspaceReplaceFileTransaction.PrepareAsync(preview, CancellationToken.None));

        Assert.AreEqual("old\n", await File.ReadAllTextAsync(firstPath));
        Assert.AreEqual("changed\n", await File.ReadAllTextAsync(secondPath));
    }
    [TestMethod]
    public async Task CommitAsync_RejectsChangeAfterPrepareBeforeWritingAnything()
    {
        var firstPath = Path.Combine(_root, "a.rocket");
        var secondPath = Path.Combine(_root, "b.rocket");
        await File.WriteAllTextAsync(firstPath, "old\n", new UTF8Encoding(false));
        await File.WriteAllTextAsync(secondPath, "old\n", new UTF8Encoding(false));
        var service = new WorkspaceSearchService();
        var preview = await service.CreateReplacePreviewAsync(
            new SearchQuery(_root, "old", maxResultsPerFile: 10),
            "new",
            CancellationToken.None);
        var transaction = await WorkspaceReplaceFileTransaction.PrepareAsync(preview, CancellationToken.None);

        await File.WriteAllTextAsync(secondPath, "changed after prepare\n", new UTF8Encoding(false));

        await Assert.ThrowsExactlyAsync<WorkspaceReplaceConflictException>(
            () => transaction.CommitAsync(CancellationToken.None));

        Assert.AreEqual("old\n", await File.ReadAllTextAsync(firstPath));
        Assert.AreEqual("changed after prepare\n", await File.ReadAllTextAsync(secondPath));
    }

}
