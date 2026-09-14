using RocketIDE.App.Editor;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class BracketHighlightingTests
{
    [TestMethod]
    public void MatcherIgnoresBracketsInsideRocketCommentsAndDoubleQuotedStrings()
    {
        var text = "fn main() { let x = \"(not code)\" # } ignored\n return x }";

        Assert.IsTrue(BracketMatcher.TryFind(text, text.IndexOf('{'), out var pair));
        Assert.AreEqual(text.IndexOf('{'), pair.OpenOffset);
        Assert.AreEqual(text.LastIndexOf('}'), pair.CloseOffset);
        Assert.IsFalse(BracketMatcher.TryFind(text, text.IndexOf('(', text.IndexOf('"')), out _));
    }

    [TestMethod]
    public void MatcherIgnoresSingleQuotedStringsAndEscapedQuotes()
    {
        var text = "fn f() { let a = '[(]'; let b = \"\\\"{\\\"\"; return (a) }";
        var returnOpen = text.IndexOf("(a)", StringComparison.Ordinal);

        Assert.IsFalse(BracketMatcher.TryFind(text, text.IndexOf('[', StringComparison.Ordinal), out _));
        Assert.IsFalse(BracketMatcher.TryFind(text, text.IndexOf('{', text.IndexOf("let b", StringComparison.Ordinal)), out _));
        Assert.IsTrue(BracketMatcher.TryFind(text, returnOpen, out var pair));
        Assert.AreEqual(returnOpen + 2, pair.CloseOffset);
    }

    [TestMethod]
    public void MatcherTreatsDoubleSlashAsOrdinaryRocketCodeCharacters()
    {
        var text = "fn f() { let x = 4 // 2; return (x) }";
        var returnOpen = text.IndexOf("(x)", StringComparison.Ordinal);

        Assert.IsTrue(BracketMatcher.TryFind(text, returnOpen, out _));
    }
}
