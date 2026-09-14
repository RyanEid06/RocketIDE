using RocketIDE.App.Editor;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class BracketHighlightingTests
{
    [TestMethod]
    public void MatcherIgnoresBracketsInsideStringsAndComments()
    {
        var text = "fn main() { let x = \"(not code)\" // }\n return x }";

        Assert.IsTrue(BracketMatcher.TryFind(text, text.IndexOf('{'), out var pair));
        Assert.AreEqual(text.IndexOf('{'), pair.OpenOffset);
        Assert.AreEqual(text.LastIndexOf('}'), pair.CloseOffset);
        Assert.IsFalse(BracketMatcher.TryFind(text, text.IndexOf('(', text.IndexOf('"')), out _));
    }
}
