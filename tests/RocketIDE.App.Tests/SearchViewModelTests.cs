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
