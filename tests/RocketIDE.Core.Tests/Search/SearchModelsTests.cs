using RocketIDE.Core.Search;

namespace RocketIDE.Core.Tests.Search;

[TestClass]
public sealed class SearchModelsTests
{
    [TestMethod]
    public void SearchQuery_UsesSafeDefaultsAndNormalizesExclusions()
    {
        var query = new SearchQuery(@"C:\work", "needle");

        Assert.AreEqual(@"C:\work", query.RootPath);
        Assert.AreEqual("needle", query.Pattern);
        Assert.IsFalse(query.CaseSensitive);
        Assert.IsFalse(query.WholeWord);
        Assert.IsFalse(query.UseRegex);
        Assert.AreEqual(10_000, query.MaxResults);
        Assert.IsTrue(query.ExcludedDirectoryNames.Contains(".git"));
        Assert.IsTrue(query.ExcludedDirectoryNames.Contains("obj"));
    }

    [TestMethod]
    public void SearchQuery_RejectsUnboundedOrInvalidLimits()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SearchQuery("", "needle"));
        Assert.ThrowsExactly<ArgumentException>(() => new SearchQuery(@"C:\work", ""));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SearchQuery(@"C:\work", "needle", maxResults: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SearchQuery(@"C:\work", "needle", maxFileBytes: 0));
    }

    [TestMethod]
    public void SearchMatch_UsesOneBasedDisplayLocationAndZeroBasedOffset()
    {
        var match = new SearchMatch(@"C:\work\main.rocket", 3, 5, 6, 22, "needle", "  let needle = value");

        Assert.AreEqual(3, match.Line);
        Assert.AreEqual(5, match.Column);
        Assert.AreEqual(22, match.StartOffset);
        Assert.AreEqual("needle", match.MatchedText);
    }

    [TestMethod]
    public void ReplacePreview_ReportsMatchCountWithoutAllowingImplicitWrites()
    {
        var preview = new ReplacePreview(
            "new",
            [new ReplacePreviewFile(
                @"C:\work\main.rocket",
                "abc",
                [new SearchMatch(@"C:\work\main.rocket", 1, 1, 3, 0, "old", "old")])]);

        Assert.AreEqual(1, preview.TotalMatches);
        Assert.AreEqual("abc", preview.Files[0].Fingerprint);
    }
}
