using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class HoverClientTests
{
    [TestMethod]
    public void ParseResponse_MapsMarkdownAndOptionalRange()
    {
        using var document = JsonDocument.Parse("""
            {
              "contents": { "kind": "markdown", "value": "`fn launch()`\n\n[Docs](rocket-doc://1.0/launch)" },
              "range": { "start": { "line": 1, "character": 2 }, "end": { "line": 1, "character": 8 } }
            }
            """);

        var hover = HoverClient.ParseResponse(document.RootElement);

        Assert.IsNotNull(hover);
        Assert.AreEqual("markdown", hover.Contents.Kind);
        StringAssert.Contains(hover.Contents.Value, "rocket-doc://1.0/launch");
        Assert.AreEqual(1, hover.Range?.Start.Line);
        Assert.AreEqual(8, hover.Range?.End.Character);
    }

    [TestMethod]
    public void ParseResponse_NormalizesLegacyStringContentsToPlaintext()
    {
        using var document = JsonDocument.Parse("""{ "contents": "hello" }""");

        var hover = HoverClient.ParseResponse(document.RootElement);

        Assert.IsNotNull(hover);
        Assert.AreEqual("plaintext", hover.Contents.Kind);
        Assert.AreEqual("hello", hover.Contents.Value);
    }
}
