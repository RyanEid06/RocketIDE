using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.Tests.LanguageServer;

[TestClass]
public sealed class RocketStatusDtoTests
{
    [TestMethod]
    public void AnalysisStatus_UsesCurrentRocketProtocolFieldNames()
    {
        const string json = """{"bytes":1234,"elapsedMilliseconds":27,"files":4,"generation":9,"invalidatedFiles":2}""";

        var status = JsonSerializer.Deserialize<RocketAnalysisStatus>(json, LspJson.Options);

        Assert.IsNotNull(status);
        Assert.AreEqual(1234L, status.Bytes);
        Assert.AreEqual(27L, status.ElapsedMilliseconds);
        Assert.AreEqual(4, status.Files);
        Assert.AreEqual(9L, status.Generation);
        Assert.AreEqual(2, status.InvalidatedFiles);
    }

    [TestMethod]
    public void ProjectStatus_ParsesCurrentProjectMetricsAndConfiguredBounds()
    {
        const string json = """{"bytes":2345,"elapsedMilliseconds":31,"files":8,"generation":10,"maximumProjectBytes":67108864,"maximumProjectFiles":4096,"symbols":42}""";

        var status = JsonSerializer.Deserialize<RocketProjectStatus>(json, LspJson.Options);

        Assert.IsNotNull(status);
        Assert.AreEqual(2345L, status.Bytes);
        Assert.AreEqual(31L, status.ElapsedMilliseconds);
        Assert.AreEqual(8, status.Files);
        Assert.AreEqual(10L, status.Generation);
        Assert.AreEqual(67108864L, status.MaximumProjectBytes);
        Assert.AreEqual(4096, status.MaximumProjectFiles);
        Assert.AreEqual(42, status.Symbols);
    }
}
