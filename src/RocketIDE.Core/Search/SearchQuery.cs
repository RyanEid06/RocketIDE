namespace RocketIDE.Core.Search;

public sealed record SearchQuery
{
    public SearchQuery(
        string rootPath,
        string pattern,
        bool caseSensitive = false,
        bool wholeWord = false,
        bool useRegex = false,
        int maxResults = 10_000,
        int maxResultsPerFile = 500,
        int maxFileBytes = 16 * 1024 * 1024,
        int previewLineLength = 400,
        IReadOnlySet<string>? excludedDirectoryNames = null,
        IReadOnlyDictionary<string, string>? inMemoryBuffers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }
        if (maxResultsPerFile <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResultsPerFile));
        }
        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        }
        if (previewLineLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previewLineLength));
        }

        RootPath = Path.GetFullPath(rootPath);
        Pattern = pattern;
        CaseSensitive = caseSensitive;
        WholeWord = wholeWord;
        UseRegex = useRegex;
        MaxResults = maxResults;
        MaxResultsPerFile = maxResultsPerFile;
        MaxFileBytes = maxFileBytes;
        PreviewLineLength = previewLineLength;
        ExcludedDirectoryNames = new HashSet<string>(
            excludedDirectoryNames ?? DefaultExcludedDirectoryNames,
            StringComparer.OrdinalIgnoreCase);
        InMemoryBuffers = inMemoryBuffers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> DefaultExcludedDirectoryNames { get; } = new HashSet<string>(
        [".git", ".rocketc", "bin", "obj", "out", ".vs"],
        StringComparer.OrdinalIgnoreCase);

    public string RootPath { get; }

    public string Pattern { get; }

    public bool CaseSensitive { get; }

    public bool WholeWord { get; }

    public bool UseRegex { get; }

    public int MaxResults { get; }

    public int MaxResultsPerFile { get; }

    public int MaxFileBytes { get; }

    public int PreviewLineLength { get; }

    public IReadOnlySet<string> ExcludedDirectoryNames { get; }

    public IReadOnlyDictionary<string, string> InMemoryBuffers { get; }
}
