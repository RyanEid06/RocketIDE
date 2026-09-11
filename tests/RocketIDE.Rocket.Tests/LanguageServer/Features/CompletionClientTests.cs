using System.Text.Json;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class CompletionClientTests
{
    [TestMethod]
    public void ParseResponse_MapsListItemDocumentationKindAndEdits()
    {
        using var document = JsonDocument.Parse("""
            {
              "isIncomplete": true,
              "items": [
                {
                  "label": "print",
                  "kind": 3,
                  "detail": "fn print(value: String)",
                  "documentation": { "kind": "markdown", "value": "Prints `value`." },
                  "insertText": "print",
                  "textEdit": {
                    "newText": "print",
                    "insert": { "start": { "line": 2, "character": 4 }, "end": { "line": 2, "character": 7 } },
                    "replace": { "start": { "line": 2, "character": 4 }, "end": { "line": 2, "character": 7 } }
                  },
                  "additionalTextEdits": [
                    {
                      "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 0 } },
                      "newText": "import rocket.io\n"
                    }
                  ]
                }
              ]
            }
            """);

        var result = CompletionClient.ParseResponse(document.RootElement);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.IsIncomplete);
        Assert.AreEqual(1, result.Items.Count);
        var item = result.Items[0];
        Assert.AreEqual("print", item.Label);
        Assert.AreEqual(3, item.Kind);
        Assert.AreEqual("fn print(value: String)", item.Detail);
        Assert.AreEqual("markdown", item.Documentation?.Kind);
        Assert.AreEqual("Prints `value`.", item.Documentation?.Value);
        Assert.AreEqual("print", item.InsertText);
        Assert.IsNotNull(item.TextEdit);
        Assert.AreEqual(2, item.TextEdit.ReplaceRange.Start.Line);
        Assert.AreEqual(1, item.AdditionalTextEdits.Count);
        Assert.AreEqual("import rocket.io\n", item.AdditionalTextEdits[0].NewText);
    }

    [TestMethod]
    public void ParseResponse_AcceptsPlainArrayAndUsesLabelAsInsertionFallback()
    {
        using var document = JsonDocument.Parse("""
            [
              { "label": "rocketValue", "documentation": "plain docs" }
            ]
            """);

        var result = CompletionClient.ParseResponse(document.RootElement);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsIncomplete);
        Assert.AreEqual("rocketValue", result.Items.Single().InsertText);
        Assert.AreEqual("plaintext", result.Items.Single().Documentation?.Kind);
    }

    [TestMethod]
    public void ParseResponse_RejectsMalformedTextEditInsteadOfApplyingAmbiguousData()
    {
        using var document = JsonDocument.Parse("""
            [
              {
                "label": "bad",
                "textEdit": { "newText": "bad" }
              }
            ]
            """);

        Assert.ThrowsExactly<LspProtocolException>(() => CompletionClient.ParseResponse(document.RootElement));
    }
}
