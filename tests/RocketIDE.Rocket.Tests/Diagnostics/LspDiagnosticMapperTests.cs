using System.Text.Json;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.Diagnostics;

namespace RocketIDE.Rocket.Tests.Diagnostics;

[TestClass]
public sealed class LspDiagnosticMapperTests
{
    [TestMethod]
    public void Map_ParsesPublishDiagnosticsIncludingVersionSeverityCodeSourceAndUtf16Range()
    {
        var path = Path.Combine(Path.GetTempPath(), "rocket diagnostics", "main.rocket");
        var uri = new Uri(path).AbsoluteUri;
        using var json = JsonDocument.Parse($$"""
        {
          "uri": {{JsonSerializer.Serialize(uri)}},
          "version": 9,
          "diagnostics": [
            {
              "range": {
                "start": { "line": 2, "character": 3 },
                "end": { "line": 2, "character": 5 }
              },
              "severity": 2,
              "code": "R4001",
              "source": "rocketc",
              "message": "Type mismatch"
            }
          ]
        }
        """);

        var publication = LspDiagnosticMapper.Map(json.RootElement, generation: 12);

        Assert.AreEqual(12L, publication.Generation);
        Assert.AreEqual(Path.GetFullPath(path), Path.GetFullPath(publication.FilePath));
        Assert.AreEqual(9, publication.Version);
        Assert.AreEqual(1, publication.Diagnostics.Count);
        var diagnostic = publication.Diagnostics[0];
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.AreEqual("R4001", diagnostic.Code);
        Assert.AreEqual("rocketc", diagnostic.Source);
        Assert.AreEqual("Type mismatch", diagnostic.Message);
        Assert.AreEqual(new SourceRange(2, 3, 2, 5), diagnostic.Range);
    }

    [TestMethod]
    public void Map_MapsAllLspSeveritiesWithoutParsingMessageText()
    {
        using var json = JsonDocument.Parse("""
        {
          "uri": "file:///C:/repo/main.rocket",
          "version": 1,
          "diagnostics": [
            { "range": { "start": {"line":0,"character":0}, "end": {"line":0,"character":1} }, "severity":1, "code":"R1001", "message":"same" },
            { "range": { "start": {"line":1,"character":0}, "end": {"line":1,"character":1} }, "severity":2, "code":"R1002", "message":"same" },
            { "range": { "start": {"line":2,"character":0}, "end": {"line":2,"character":1} }, "severity":3, "code":"R1003", "message":"same" },
            { "range": { "start": {"line":3,"character":0}, "end": {"line":3,"character":1} }, "severity":4, "code":"R1004", "message":"same" }
          ]
        }
        """);

        var publication = LspDiagnosticMapper.Map(json.RootElement, generation: 1);

        CollectionAssert.AreEqual(
            new[] { DiagnosticSeverity.Error, DiagnosticSeverity.Warning, DiagnosticSeverity.Information, DiagnosticSeverity.Hint },
            publication.Diagnostics.Select(item => item.Severity).ToArray());
    }

    [TestMethod]
    public void Map_AllowsOptionalVersionAndNumericCode()
    {
        using var json = JsonDocument.Parse("""
        {
          "uri": "file:///C:/repo/main.rocket",
          "diagnostics": [
            { "range": { "start": {"line":0,"character":0}, "end": {"line":0,"character":1} }, "code": 42, "message":"message" }
          ]
        }
        """);

        var publication = LspDiagnosticMapper.Map(json.RootElement, generation: 3);

        Assert.IsNull(publication.Version);
        Assert.AreEqual("42", publication.Diagnostics[0].Code);
        Assert.AreEqual(DiagnosticSeverity.Information, publication.Diagnostics[0].Severity);
    }
}
