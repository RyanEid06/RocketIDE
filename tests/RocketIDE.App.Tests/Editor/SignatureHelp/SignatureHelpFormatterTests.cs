using RocketIDE.App.Editor.SignatureHelp;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Tests.Editor.SignatureHelp;

[TestClass]
public sealed class SignatureHelpFormatterTests
{
    [TestMethod]
    public void GetActiveParameterRange_PrefersServerOffsetLabel()
    {
        var signature = new RocketSignatureInformation(
            "draw_rect(x: Float, y: Float = 0)",
            null,
            new[]
            {
                new RocketParameterInformation(null, 10, 18, null),
                new RocketParameterInformation("y: Float = 0", null, null, null),
            },
            0);
        var help = new RocketSignatureHelp(new[] { signature }, 0, 0);

        var range = SignatureHelpFormatter.GetActiveParameterRange(help);

        Assert.IsNotNull(range);
        Assert.AreEqual(10, range.Value.Start);
        Assert.AreEqual(8, range.Value.Length);
    }

    [TestMethod]
    public void GetActiveParameterRange_FindsStringLabelIncludingDefaultExpression()
    {
        var signature = new RocketSignatureInformation(
            "draw_rect(x: Float, y: Float = 0)",
            null,
            new[] { new RocketParameterInformation("y: Float = 0", null, null, null) },
            0);
        var help = new RocketSignatureHelp(new[] { signature }, 0, 0);

        var range = SignatureHelpFormatter.GetActiveParameterRange(help);

        Assert.IsNotNull(range);
        Assert.AreEqual("y: Float = 0", signature.Label.Substring(range.Value.Start, range.Value.Length));
    }
}
