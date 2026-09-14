using System.Security.Cryptography;
using System.Text;
using RocketIDE.Core.Search;

namespace RocketIDE.App.Integration;

public static class WorkspaceReplaceText
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string ComputeFingerprint(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(text)));
    }

    public static string Apply(string text, IReadOnlyList<SearchMatch> matches, string replacement)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(replacement);

        var builder = new StringBuilder(text);
        foreach (var match in matches.OrderByDescending(match => match.StartOffset))
        {
            if (match.StartOffset < 0 || match.StartOffset + match.Length > builder.Length ||
                !string.Equals(builder.ToString(match.StartOffset, match.Length), match.MatchedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The open editor buffer changed after the replace preview was created: '{match.FilePath}'.");
            }

            builder.Remove(match.StartOffset, match.Length);
            builder.Insert(match.StartOffset, replacement);
        }

        return builder.ToString();
    }
}
