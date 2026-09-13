using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class RocketLanguageServerCapabilitiesTests
{
    [TestMethod]
    public void Parse_ReadsFeatureTriggersAndSemanticLegend()
    {
        using var document = JsonDocument.Parse("""
            {
              "completionProvider": { "triggerCharacters": [".", ":"] },
              "hoverProvider": true,
              "signatureHelpProvider": { "triggerCharacters": ["(", ","], "retriggerCharacters": [")"] },
              "semanticTokensProvider": {
                "legend": {
                  "tokenTypes": ["keyword", "function", "parameter"],
                  "tokenModifiers": ["declaration", "readonly"]
                },
                "full": { "delta": true }
              }
            }
            """);

        var capabilities = RocketLanguageServerCapabilities.Parse(document.RootElement);

        Assert.IsTrue(capabilities.SupportsCompletion);
        CollectionAssert.AreEqual(new[] { ".", ":" }, capabilities.CompletionTriggerCharacters.ToArray());
        Assert.IsTrue(capabilities.SupportsHover);
        Assert.IsTrue(capabilities.SupportsSignatureHelp);
        CollectionAssert.AreEqual(new[] { "(", "," }, capabilities.SignatureTriggerCharacters.ToArray());
        CollectionAssert.AreEqual(new[] { ")" }, capabilities.SignatureRetriggerCharacters.ToArray());
        Assert.IsTrue(capabilities.SupportsSemanticTokens);
        Assert.IsTrue(capabilities.SupportsSemanticTokenDelta);
        CollectionAssert.AreEqual(new[] { "keyword", "function", "parameter" }, capabilities.SemanticTokenLegend.TokenTypes.ToArray());
        CollectionAssert.AreEqual(new[] { "declaration", "readonly" }, capabilities.SemanticTokenLegend.TokenModifiers.ToArray());
    }

    [TestMethod]
    public void Parse_DisablesMissingOrFalseFeaturesWithoutInventingFallbacks()
    {
        using var document = JsonDocument.Parse("""
            {
              "hoverProvider": false,
              "semanticTokensProvider": null
            }
            """);

        var capabilities = RocketLanguageServerCapabilities.Parse(document.RootElement);

        Assert.IsFalse(capabilities.SupportsCompletion);
        Assert.IsFalse(capabilities.SupportsHover);
        Assert.IsFalse(capabilities.SupportsSignatureHelp);
        Assert.IsFalse(capabilities.SupportsSemanticTokens);
        Assert.AreEqual(0, capabilities.CompletionTriggerCharacters.Count);
        Assert.AreEqual(0, capabilities.SemanticTokenLegend.TokenTypes.Count);
    }


    [TestMethod]
    public void Parse_ReadsWp09BooleanAndOptionsCapabilitiesIncludingPrepareRename()
    {
        using var document = JsonDocument.Parse("""
        {
          "definitionProvider": { "workDoneProgress": true },
          "referencesProvider": true,
          "renameProvider": { "prepareProvider": true },
          "codeActionProvider": { "codeActionKinds": ["quickfix"] },
          "documentFormattingProvider": true
        }
        """);

        var capabilities = RocketLanguageServerCapabilities.Parse(document.RootElement);

        Assert.IsTrue(capabilities.SupportsDefinition);
        Assert.IsTrue(capabilities.SupportsReferences);
        Assert.IsTrue(capabilities.SupportsRename);
        Assert.IsTrue(capabilities.SupportsPrepareRename);
        Assert.IsTrue(capabilities.SupportsCodeActions);
        Assert.IsTrue(capabilities.SupportsDocumentFormatting);
    }

    [TestMethod]
    public void Parse_Wp09ProvidersRejectMalformedNonBooleanNonOptionsShapes()
    {
        using var document = JsonDocument.Parse("""
        {
          "definitionProvider": "yes",
          "referencesProvider": 1,
          "renameProvider": [],
          "codeActionProvider": "quickfix",
          "documentFormattingProvider": 1
        }
        """);

        var capabilities = RocketLanguageServerCapabilities.Parse(document.RootElement);

        Assert.IsFalse(capabilities.SupportsDefinition);
        Assert.IsFalse(capabilities.SupportsReferences);
        Assert.IsFalse(capabilities.SupportsRename);
        Assert.IsFalse(capabilities.SupportsPrepareRename);
        Assert.IsFalse(capabilities.SupportsCodeActions);
        Assert.IsFalse(capabilities.SupportsDocumentFormatting);
    }

    [TestMethod]
    public void Parse_RenameBooleanTrueDoesNotInventPrepareRenameSupport()
    {
        using var document = JsonDocument.Parse("""{ "renameProvider": true }""");

        var capabilities = RocketLanguageServerCapabilities.Parse(document.RootElement);

        Assert.IsTrue(capabilities.SupportsRename);
        Assert.IsFalse(capabilities.SupportsPrepareRename);
    }
}
