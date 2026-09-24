using System.Text.Json;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class FoldingClientTests
{
    [TestMethod]
    public void Parse_AcceptsOptionalCharactersAndKind()
    {
        using var json = JsonDocument.Parse("""[{"startLine":1,"startCharacter":2,"endLine":5,"endCharacter":0,"kind":"region"}]""");

        var result = FoldingClient.Parse(json.RootElement);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[0].StartLine);
        Assert.AreEqual(2, result[0].StartCharacter);
        Assert.AreEqual(5, result[0].EndLine);
        Assert.AreEqual("region", result[0].Kind);
    }

    [TestMethod]
    public void Parse_RejectsBackwardsRange()
    {
        using var json = JsonDocument.Parse("""[{"startLine":5,"endLine":2}]""");
        Assert.ThrowsExactly<LspProtocolException>(() => FoldingClient.Parse(json.RootElement));
    }
}
