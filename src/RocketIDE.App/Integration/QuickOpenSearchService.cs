using System.IO;

namespace RocketIDE.App.Integration;

public sealed record QuickOpenMatch(
    string FullPath,
    string RelativePath,
    double Score,
    bool IsOpen,
    bool IsRecent)
{
    public string FileName => Path.GetFileName(FullPath);
}

public sealed class QuickOpenSearchService(QuickOpenFileFinder finder)
{
    private readonly QuickOpenFileFinder _finder = finder ?? throw new ArgumentNullException(nameof(finder));

    public Task<IReadOnlyList<string>> DiscoverAsync(
        string rootPath,
        int maxFiles,
        CancellationToken cancellationToken) =>
        _finder.FindAsync(rootPath, maxFiles, cancellationToken);

    public Task<IReadOnlyList<QuickOpenMatch>> SearchAsync(
        string rootPath,
        IReadOnlyList<string> files,
        string query,
        IReadOnlyCollection<string> openPaths,
        IReadOnlyList<string> recentPaths,
        int maxResults,
        CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<QuickOpenMatch>>(
            () => QuickOpenRanker.Rank(rootPath, files, query, openPaths, recentPaths, maxResults, cancellationToken),
            cancellationToken);
}

public static class QuickOpenRanker
{
    public static IReadOnlyList<QuickOpenMatch> Rank(
        string rootPath,
        IReadOnlyList<string> files,
        string query,
        IReadOnlyCollection<string> openPaths,
        IReadOnlyList<string> recentPaths,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(openPaths);
        ArgumentNullException.ThrowIfNull(recentPaths);
        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        var root = Path.GetFullPath(rootPath);
        var comparer = PathComparer;
        var open = new HashSet<string>(openPaths.Select(Path.GetFullPath), comparer);
        var recent = new Dictionary<string, int>(comparer);
        for (var index = 0; index < recentPaths.Count; index++)
        {
            var normalized = Path.GetFullPath(recentPaths[index]);
            recent.TryAdd(normalized, index);
        }

        var normalizedQuery = query.Trim();
        var matches = new List<QuickOpenMatch>(Math.Min(files.Count, maxResults * 4));
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            var relativePath = Path.GetRelativePath(root, fullPath);
            var fileName = Path.GetFileName(fullPath);
            var score = ScoreMatch(fileName, relativePath, normalizedQuery);
            if (double.IsNegativeInfinity(score))
            {
                continue;
            }

            var isOpen = open.Contains(fullPath);
            var isRecent = recent.TryGetValue(fullPath, out var recentIndex);
            if (isOpen)
            {
                score += 240;
            }
            if (isRecent)
            {
                score += Math.Max(20, 140 - Math.Min(recentIndex, 120));
            }
            matches.Add(new QuickOpenMatch(fullPath, relativePath, score, isOpen, isRecent));
        }

        return matches
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.RelativePath.Length)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();
    }

    private static double ScoreMatch(string fileName, string relativePath, string query)
    {
        if (query.Length == 0)
        {
            return 0;
        }

        if (string.Equals(fileName, query, StringComparison.OrdinalIgnoreCase))
        {
            return 1200;
        }
        if (fileName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1000 - Math.Min(100, fileName.Length - query.Length);
        }
        if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 800 - Math.Min(120, fileName.Length - query.Length);
        }
        if (relativePath.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 720 - Math.Min(120, relativePath.Length - query.Length);
        }
        if (relativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 620 - Math.Min(140, relativePath.Length - query.Length);
        }

        var fileScore = FuzzyScore(fileName, query);
        var pathScore = FuzzyScore(relativePath, query);
        if (fileScore < 0 && pathScore < 0)
        {
            return double.NegativeInfinity;
        }
        return Math.Max(fileScore >= 0 ? 500 + fileScore : double.NegativeInfinity,
            pathScore >= 0 ? 380 + pathScore : double.NegativeInfinity);
    }

    private static int FuzzyScore(string candidate, string query)
    {
        var candidateIndex = 0;
        var lastMatch = -2;
        var score = 0;
        for (var queryIndex = 0; queryIndex < query.Length; queryIndex++)
        {
            var expected = char.ToUpperInvariant(query[queryIndex]);
            var found = -1;
            for (; candidateIndex < candidate.Length; candidateIndex++)
            {
                if (char.ToUpperInvariant(candidate[candidateIndex]) == expected)
                {
                    found = candidateIndex;
                    candidateIndex++;
                    break;
                }
            }
            if (found < 0)
            {
                return -1;
            }

            score += 8;
            if (found == lastMatch + 1)
            {
                score += 14;
            }
            if (found == 0 || IsBoundary(candidate[found - 1]))
            {
                score += 10;
            }
            score -= Math.Min(5, Math.Max(0, found - lastMatch - 1));
            lastMatch = found;
        }
        return score - Math.Min(80, candidate.Length - query.Length);
    }

    private static bool IsBoundary(char character) =>
        character is '/' or '\\' or '_' or '-' or '.' or ' ';

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
