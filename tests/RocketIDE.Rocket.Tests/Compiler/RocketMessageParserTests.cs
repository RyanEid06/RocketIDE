using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.Compiler;
using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Tests.Compiler;

[TestClass]
public sealed class RocketMessageParserTests
{
    [TestMethod]
    public void TryParse_ParsesDiagnosticAndOneBasedSpan()
    {
        const string json = """
            {"schema":"rocket-message-1","reason":"diagnostic","level":"error","code":"R4002","message":"undefined name","span":{"file":"src/main.rocket","line":12,"column":9}}
            """;

        Assert.IsTrue(RocketMessageParser.TryParse(json, out var message));
        Assert.IsNotNull(message);
        Assert.AreEqual("diagnostic", message.Reason);
        Assert.AreEqual("R4002", message.Code);
        Assert.AreEqual(12, message.Span?.Line);
        Assert.AreEqual(9, message.Span?.Column);

        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        var target = new RocketTarget(Path.Combine(temp.Path, "src", "main.rocket"), temp.Path, Path.Combine(temp.Path, "rocket.toml"), false);
        Assert.IsTrue(RocketMessageParser.TryMapDiagnostic(message, target, out var diagnostic));
        Assert.IsNotNull(diagnostic);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(temp.Path, "src", "main.rocket")), diagnostic.FilePath);
        Assert.AreEqual(new SourceRange(11, 8, 11, 9), diagnostic.Range);
        Assert.AreEqual("Compiler", diagnostic.Provenance);
    }

    [TestMethod]
    public void TryMapDiagnostic_UsesCommandProvenanceWhenProvided()
    {
        using var temp = new TempDirectory();
        var target = new RocketTarget(Path.Combine(temp.Path, "main.rocket"), temp.Path, null, true);
        var message = new RocketMessage(
            "diagnostic",
            Level: "warning",
            Code: "R2001",
            Message: "bad syntax",
            Span: new RocketMessageSpan("main.rocket", 2, 3));

        Assert.IsTrue(RocketMessageParser.TryMapDiagnostic(message, target, out var diagnostic, "Build"));
        Assert.IsNotNull(diagnostic);
        Assert.AreEqual("Build", diagnostic.Provenance);
    }

    [TestMethod]
    public void TryParse_ParsesBuildFinished()
    {
        const string json = """
            {"schema":"rocket-message-1","reason":"build-finished","command":"build","success":true,"artifact":".rocketc/demo.exe","cache":"hit"}
            """;

        Assert.IsTrue(RocketMessageParser.TryParse(json, out var message));
        Assert.AreEqual("build", message!.Command);
        Assert.AreEqual(true, message.Success);
        Assert.AreEqual(".rocketc/demo.exe", message.Artifact);
        Assert.AreEqual("hit", message.Cache);
        StringAssert.Contains(RocketMessageParser.FormatForOutput(message), "cache hit");
    }

    [TestMethod]
    public void TryParse_ParsesTestEventsAndSummary()
    {
        Assert.IsTrue(RocketMessageParser.TryParse("{\"schema\":\"rocket-message-1\",\"reason\":\"test-started\",\"name\":\"math\"}", out var started));
        Assert.AreEqual("math", started!.Name);

        Assert.IsTrue(RocketMessageParser.TryParse("{\"schema\":\"rocket-message-1\",\"reason\":\"test-finished\",\"name\":\"math\",\"status\":\"pass\",\"exitCode\":0}", out var finished));
        Assert.AreEqual("pass", finished!.Status);
        Assert.AreEqual(0, finished.ExitCode);

        Assert.IsTrue(RocketMessageParser.TryParse("{\"schema\":\"rocket-message-1\",\"reason\":\"test-summary\",\"passed\":4,\"failed\":1,\"expectedFailures\":2,\"selected\":7}", out var summary));
        Assert.AreEqual(4, summary!.Passed);
        Assert.AreEqual(1, summary.Failed);
        Assert.AreEqual(2, summary.ExpectedFailures);
        Assert.AreEqual(7, summary.Selected);
    }

    [TestMethod]
    public void TryParse_AllowsMissingOptionalFields()
    {
        Assert.IsTrue(RocketMessageParser.TryParse("{\"schema\":\"rocket-message-1\",\"reason\":\"build-finished\"}", out var message));
        Assert.IsNotNull(message);
        Assert.IsNull(message.Artifact);
        Assert.IsNull(message.Success);
        Assert.AreEqual("build finished", RocketMessageParser.FormatForOutput(message));
    }

    [TestMethod]
    public void TryParse_RejectsMalformedJsonAndUnknownSchema()
    {
        Assert.IsFalse(RocketMessageParser.TryParse("{not-json", out _));
        Assert.IsFalse(RocketMessageParser.TryParse("{\"schema\":\"future-schema\",\"reason\":\"diagnostic\"}", out _));
        Assert.IsFalse(RocketMessageParser.TryParse("ordinary output", out _));
    }

    [TestMethod]
    public void TryMapDiagnostic_RejectsMissingOrInvalidOneBasedSpan()
    {
        var target = new RocketTarget(Path.GetFullPath("main.rocket"), Path.GetFullPath("."), null, true);
        var missing = new RocketMessage("diagnostic", Level: "error", Code: "R4002", Message: "bad");
        Assert.IsFalse(RocketMessageParser.TryMapDiagnostic(missing, target, out _));

        var invalid = missing with { Span = new RocketMessageSpan("main.rocket", 0, 1) };
        Assert.IsFalse(RocketMessageParser.TryMapDiagnostic(invalid, target, out _));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-msg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
