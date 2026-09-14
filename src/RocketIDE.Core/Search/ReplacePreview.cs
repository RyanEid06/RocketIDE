namespace RocketIDE.Core.Search;

public sealed record ReplacePreviewFile
{
    public ReplacePreviewFile(
        string filePath,
        string fingerprint,
        IReadOnlyList<SearchMatch> matches)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(matches);
        FilePath = filePath;
        Fingerprint = fingerprint;
        Matches = matches;
    }

    public string FilePath { get; }

    public string Fingerprint { get; }

    public IReadOnlyList<SearchMatch> Matches { get; }
}

public sealed record ReplacePreview
{
    public ReplacePreview(string replacement, IReadOnlyList<ReplacePreviewFile> files)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(files);
        Replacement = replacement;
        Files = files;
    }

    public string Replacement { get; }

    public IReadOnlyList<ReplacePreviewFile> Files { get; }

    public int TotalMatches => Files.Sum(file => file.Matches.Count);
}

public sealed record ReplaceApplyResult(int FilesChanged, int MatchesReplaced);
