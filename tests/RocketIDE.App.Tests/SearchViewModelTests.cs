using RocketIDE.App.ViewModels;
using RocketIDE.Core.Search;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class SearchViewModelTests
{
    [TestMethod]
    public async Task SearchAsync_StreamsResultsAndUpdatesStatus()
    {
        var service = new FakeSearchService
        {
            SearchImplementation = (_, progress, _) =>
            {
                progress.Report(new SearchResultBatch(
                    [new SearchMatch(@"C:\work\main.rocket", 2, 1, 6, 10, "needle", "needle")],
                    1,
                    0,
                    true));
                return Task.FromResult(new SearchSummary(1, 0, 1, false));
            },
        };
        var viewModel = new SearchViewModel(service)
        {
            RootPath = @"C:\work",
            Pattern = "needle",
        };

        await viewModel.SearchAsync(CancellationToken.None);

        Assert.AreEqual(1, viewModel.Results.Count);
        Assert.AreEqual("1 match in 1 file", viewModel.StatusText);
        Assert.IsFalse(viewModel.IsSearching);
    }

    [TestMethod]
    public async Task ApplyReplaceAsync_RequiresPreviewAndReportsAppliedCount()
    {
        var service = new FakeSearchService
        {
            PreviewImplementation = (_, _, _) => Task.FromResult(new ReplacePreview(
                "new",
                [new ReplacePreviewFile(@"C:\work\main.rocket", "hash", [new SearchMatch(@"C:\work\main.rocket", 1, 1, 3, 0, "old", "old")])])),
            ApplyImplementation = (_, _) => Task.FromResult(new ReplaceApplyResult(1, 1)),
        };
        var viewModel = new SearchViewModel(service)
        {
            RootPath = @"C:\work",
            Pattern = "old",
            Replacement = "new",
        };

        await viewModel.PreviewReplaceAsync(CancellationToken.None);
        var result = await viewModel.ApplyReplaceAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result!.MatchesReplaced);
        StringAssert.Contains(viewModel.StatusText, "1 match replaced");
    }


    [TestMethod]
    public async Task CancelCurrentOperation_CancelsRunningSearchAndRestoresIdleState()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeSearchService
        {
            SearchImplementation = async (_, _, token) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new SearchSummary(0, 0, 0, false);
            },
        };
        var viewModel = new SearchViewModel(service)
        {
            RootPath = @"C:\work",
            Pattern = "needle",
        };

        var running = viewModel.SearchAsync(CancellationToken.None);
        await started.Task;
        Assert.IsTrue(viewModel.CanCancel);

        viewModel.CancelCurrentOperation();
        await running;

        Assert.IsFalse(viewModel.IsSearching);
        Assert.IsFalse(viewModel.CanCancel);
        StringAssert.Contains(viewModel.StatusText, "cancelled");
    }

    [TestMethod]
    public async Task PreviewReplaceAsync_ExposesExactRowsAndOptionChangesInvalidatePreview()
    {
        var match = new SearchMatch(@"C:\work\main.rocket", 4, 7, 3, 22, "old", "value old here");
        var service = new FakeSearchService
        {
            PreviewImplementation = (_, _, _) => Task.FromResult(new ReplacePreview(
                "new",
                [new ReplacePreviewFile(@"C:\work\main.rocket", "hash", [match])])),
        };
        var viewModel = new SearchViewModel(service)
        {
            RootPath = @"C:\work",
            Pattern = "old",
            Replacement = "new",
        };

        await viewModel.PreviewReplaceAsync(CancellationToken.None);

        Assert.AreEqual(1, viewModel.ReplacePreviewRows.Count);
        Assert.AreEqual(@"C:\work\main.rocket", viewModel.ReplacePreviewRows[0].FilePath);
        Assert.AreEqual("4:7", viewModel.ReplacePreviewRows[0].Location);
        Assert.AreEqual("old", viewModel.ReplacePreviewRows[0].MatchedText);
        Assert.AreEqual("new", viewModel.ReplacePreviewRows[0].ReplacementText);
        Assert.IsTrue(viewModel.CanApplyReplace);

        viewModel.CaseSensitive = true;

        Assert.AreEqual(0, viewModel.ReplacePreviewRows.Count);
        Assert.IsFalse(viewModel.CanApplyReplace);
    }

    [TestMethod]
    public async Task ApplyReplaceAsync_ReportsUnauthorizedAccessInsteadOfThrowing()
    {
        var service = new FakeSearchService
        {
            PreviewImplementation = (_, _, _) => Task.FromResult(new ReplacePreview(
                "new",
                [new ReplacePreviewFile(@"C:\work\main.rocket", "hash", [new SearchMatch(@"C:\work\main.rocket", 1, 1, 3, 0, "old", "old")])])),
            ApplyImplementation = (_, _) => throw new UnauthorizedAccessException("Access denied"),
        };
        var viewModel = new SearchViewModel(service)
        {
            RootPath = @"C:\work",
            Pattern = "old",
            Replacement = "new",
        };

        await viewModel.PreviewReplaceAsync(CancellationToken.None);
        var result = await viewModel.ApplyReplaceAsync(CancellationToken.None);

        Assert.IsNull(result);
        StringAssert.Contains(viewModel.StatusText, "Replace not applied");
        StringAssert.Contains(viewModel.StatusText, "Access denied");
    }

    [TestMethod]
    public async Task ApplyReplaceAsync_UsesCoordinatorWhenProvided()
    {
        var preview = new ReplacePreview(
            "new",
            [new ReplacePreviewFile(@"C:\work\main.rocket", "hash", [new SearchMatch(@"C:\work\main.rocket", 1, 1, 3, 0, "old", "old")], true)]);
        var service = new FakeSearchService
        {
            PreviewImplementation = (_, _, _) => Task.FromResult(preview),
            ApplyImplementation = (_, _) => throw new AssertFailedException("Direct service apply must not be used when a coordinator is configured."),
        };
        var applied = false;
        var viewModel = new SearchViewModel(service)
        {
            RootPath = @"C:\work",
            Pattern = "old",
            Replacement = "new",
            ReplaceApplier = (received, _) =>
            {
                Assert.AreSame(preview, received);
                applied = true;
                return Task.FromResult(new ReplaceApplyResult(1, 1));
            },
        };

        await viewModel.PreviewReplaceAsync(CancellationToken.None);
        var result = await viewModel.ApplyReplaceAsync(CancellationToken.None);

        Assert.IsTrue(applied);
        Assert.AreEqual(1, result!.MatchesReplaced);
    }

    private sealed class FakeSearchService : IWorkspaceSearchService
    {
        public Func<SearchQuery, IProgress<SearchResultBatch>, CancellationToken, Task<SearchSummary>>? SearchImplementation { get; init; }
        public Func<SearchQuery, string, CancellationToken, Task<ReplacePreview>>? PreviewImplementation { get; init; }
        public Func<ReplacePreview, CancellationToken, Task<ReplaceApplyResult>>? ApplyImplementation { get; init; }

        public Task<SearchSummary> SearchAsync(SearchQuery query, IProgress<SearchResultBatch> progress, CancellationToken cancellationToken) =>
            SearchImplementation?.Invoke(query, progress, cancellationToken) ?? Task.FromResult(new SearchSummary(0, 0, 0, false));

        public Task<ReplacePreview> CreateReplacePreviewAsync(SearchQuery query, string replacement, CancellationToken cancellationToken) =>
            PreviewImplementation?.Invoke(query, replacement, cancellationToken) ?? Task.FromResult(new ReplacePreview(replacement, []));

        public Task<ReplaceApplyResult> ApplyReplaceAsync(ReplacePreview preview, CancellationToken cancellationToken) =>
            ApplyImplementation?.Invoke(preview, cancellationToken) ?? Task.FromResult(new ReplaceApplyResult(0, 0));
    }
}
