using RocketIDE.App.Editor.Hover;

namespace RocketIDE.App.Tests.Editor.Hover;

[TestClass]
public sealed class SafeMarkdownParserTests
{
    [TestMethod]
    public void Parse_KeepsHtmlLiteralAndExposesLinksWithoutExecutableBehavior()
    {
        var runs = SafeMarkdownParser.Parse("Call `launch` <script>alert(1)</script> [Docs](rocket-doc://1.0/launch) [Web](https://example.com)");

        Assert.IsTrue(runs.Any(run => run.Kind == HoverRunKind.Code && run.Text == "launch"));
        Assert.IsTrue(runs.Any(run => run.Text.Contains("<script>", StringComparison.Ordinal)));
        Assert.IsTrue(runs.Any(run => run.Kind == HoverRunKind.Link && run.Target == "rocket-doc://1.0/launch"));
        Assert.IsTrue(runs.Any(run => run.Kind == HoverRunKind.Link && run.Target == "https://example.com"));
        Assert.IsFalse(runs.Any(run => run.Kind == HoverRunKind.Html));
    }
}
