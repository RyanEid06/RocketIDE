namespace RocketIDE.App.Integration;

public static class FuzzyMatcher
{
    public static int Score(string query, string candidate)
    {
        query ??= string.Empty;
        candidate ??= string.Empty;
        if (query.Length == 0) return 0;
        if (candidate.Length == 0) return int.MinValue;

        if (string.Equals(query, candidate, StringComparison.OrdinalIgnoreCase)) return 100000;
        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 90000 - candidate.Length;

        var contains = candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (contains >= 0) return 80000 - contains * 10 - candidate.Length;

        var score = 0;
        var queryIndex = 0;
        var previousMatch = -2;
        for (var i = 0; i < candidate.Length && queryIndex < query.Length; i++)
        {
            if (char.ToUpperInvariant(candidate[i]) != char.ToUpperInvariant(query[queryIndex])) continue;
            score += previousMatch == i - 1 ? 25 : 10;
            if (i == 0 || !char.IsLetterOrDigit(candidate[i - 1])) score += 20;
            previousMatch = i;
            queryIndex++;
        }

        return queryIndex == query.Length ? score - candidate.Length : int.MinValue;
    }
}
