using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Editor.SemanticTokens;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Tests.Editor.SemanticTokens;

[TestClass]
public sealed class SemanticTokenMarkerCollectionTests
{
    [TestMethod]
    public void Update_MapsUtf16TokenSpansAndRejectsInvalidRanges()
    {
        var document = new TextDocument("fn 🚀rocket\nvalue");
        var markers = new SemanticTokenMarkerCollection();
        var tokens = new[]
        {
            new RocketSemanticToken(0, 0, 2, "keyword", Array.Empty<string>()),
            new RocketSemanticToken(0, 5, 6, "function", new[] { "declaration" }),
            new RocketSemanticToken(9, 0, 4, "variable", Array.Empty<string>()),
        };

        markers.Update(document, tokens);

        Assert.AreEqual(2, markers.Markers.Count);
        Assert.AreEqual("fn", document.GetText(markers.Markers[0].Offset, markers.Markers[0].Length));
        Assert.AreEqual("rocket", document.GetText(markers.Markers[1].Offset, markers.Markers[1].Length));
    }
}
