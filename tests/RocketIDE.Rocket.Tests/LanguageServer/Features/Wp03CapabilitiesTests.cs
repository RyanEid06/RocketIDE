using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class Wp03CapabilitiesTests
{
    [TestMethod]
    public void Parse_ReadsSymbolAndFoldingCapabilitiesWithoutGuessing()
    {
        using var json = JsonDocument.Parse("""
        {
          "documentSymbolProvider": true,
          "workspaceSymbolProvider": {"workDoneProgress":true},
          "foldingRangeProvider": true
        }
        """);
        var result = RocketLanguageServerCapabilities.Parse(json.RootElement);
        Assert.IsTrue(result.SupportsDocumentSymbols);
        Assert.IsTrue(result.SupportsWorkspaceSymbols);
        Assert.IsTrue(result.SupportsFoldingRanges);
    }

    [TestMethod]
    public void Parse_RejectsMalformedSymbolAndFoldingCapabilityShapes()
    {
        using var json = JsonDocument.Parse("""
        {
          "documentSymbolProvider":"yes",
          "workspaceSymbolProvider":1,
          "foldingRangeProvider":[]
        }
        """);
        var result = RocketLanguageServerCapabilities.Parse(json.RootElement);
        Assert.IsFalse(result.SupportsDocumentSymbols);
        Assert.IsFalse(result.SupportsWorkspaceSymbols);
        Assert.IsFalse(result.SupportsFoldingRanges);
    }
}
