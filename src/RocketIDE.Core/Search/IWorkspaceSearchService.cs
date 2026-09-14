namespace RocketIDE.Core.Search;

public interface IWorkspaceSearchService
{
    Task<SearchSummary> SearchAsync(
        SearchQuery query,
        IProgress<SearchResultBatch> progress,
        CancellationToken cancellationToken);

    Task<ReplacePreview> CreateReplacePreviewAsync(
        SearchQuery query,
        string replacement,
        CancellationToken cancellationToken);

    Task<ReplaceApplyResult> ApplyReplaceAsync(
        ReplacePreview preview,
        CancellationToken cancellationToken);
}
