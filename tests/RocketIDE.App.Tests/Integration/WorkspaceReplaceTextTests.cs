using RocketIDE.App.Integration;
using RocketIDE.Core.Search;

namespace RocketIDE.App.Tests.Integration;

[TestClass]
public sealed class WorkspaceReplaceTextTests
{
    [TestMethod]
    public void Apply_UsesPreviewOffsetsFromEndAndRejectsChangedText()
    {
        const string text = "old and old";
        var matches = new[]
        {
            new SearchMatch("x.rocket", 1, 1, 3, 0, "old", text),
            new SearchMatch("x.rocket", 1, 9, 3, 8, "old", text),
        };

        var replaced = WorkspaceReplaceText.Apply(text, matches, "new");

        Assert.AreEqual("new and new", replaced);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            WorkspaceReplaceText.Apply("changed", matches, "new"));
    }

    [TestMethod]
    public void Fingerprint_IsStableForExactBufferText()
    {
        var first = WorkspaceReplaceText.ComputeFingerprint("abc\n");
        var second = WorkspaceReplaceText.ComputeFingerprint("abc\n");
        var changed = WorkspaceReplaceText.ComputeFingerprint("abc\r\n");

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, changed);
    }
}
