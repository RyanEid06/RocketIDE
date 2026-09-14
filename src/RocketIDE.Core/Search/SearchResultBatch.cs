namespace RocketIDE.Core.Search;

public sealed record SearchResultBatch(
    IReadOnlyList<SearchMatch> Matches,
    int FilesScanned,
    int FilesSkipped,
    bool IsComplete,
    bool ReachedResultLimit = false)
{
    public int MatchCount => Matches.Count;
}

public sealed record SearchSummary(
    int FilesScanned,
    int FilesSkipped,
    int TotalMatches,
    bool ReachedResultLimit);
