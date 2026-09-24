using System.IO;
using System.Text.Json;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class SymbolClientTests
{
    [TestMethod]
    public void ParseDocumentSymbols_PreservesHierarchyAndSelectionRange()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "main.rocket"));
        using var json = JsonDocument.Parse("""
        [
          {
            "name":"Pair","kind":23,
            "range":{"start":{"line":0,"character":0},"end":{"line":4,"character":0}},
            "selectionRange":{"start":{"line":0,"character":7},"end":{"line":0,"character":11}},
            "children":[
              {
                "name":"first","kind":8,
                "range":{"start":{"line":1,"character":4},"end":{"line":1,"character":14}},
                "selectionRange":{"start":{"line":1,"character":4},"end":{"line":1,"character":9}}
              }
            ]
          }
        ]
        """);

        var result = SymbolClient.ParseDocumentSymbols(json.RootElement, path);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Pair", result[0].Name);
        Assert.AreEqual(1, result[0].Children.Count);
        Assert.AreEqual("first", result[0].Children[0].Name);
        Assert.AreEqual(7, result[0].SelectionRange.Start.Character);
    }

    [TestMethod]
    public void ParseDocumentSymbols_AcceptsFlatSymbolInformation()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "flat.rocket"));
        var uri = new Uri(path).AbsoluteUri;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[]
        {
            new
            {
                name = "main",
                kind = 12,
                location = new
                {
                    uri,
                    range = new
                    {
                        start = new { line = 2, character = 3 },
                        end = new { line = 2, character = 7 },
                    },
                },
            },
        }));

        var result = SymbolClient.ParseDocumentSymbols(json.RootElement, path);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("main", result[0].Name);
        Assert.AreEqual(result[0].Range, result[0].SelectionRange);
        Assert.AreEqual(0, result[0].Children.Count);
    }

    [TestMethod]
    public void ParseWorkspaceSymbols_BoundsResults()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "workspace.rocket"));
        var uri = new Uri(path).AbsoluteUri;
        var items = Enumerable.Range(0, 1100)
            .Select(index => new
            {
                name = $"s{index}",
                kind = 12,
                location = new { uri, range = new { start = new { line = index, character = 0 }, end = new { line = index, character = 1 } } },
            })
            .ToArray();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(items));

        var result = SymbolClient.ParseWorkspaceSymbols(json.RootElement);

        Assert.AreEqual(1024, result.Count);
    }
}
