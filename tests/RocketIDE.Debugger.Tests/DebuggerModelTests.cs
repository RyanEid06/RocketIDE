using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class DebuggerModelTests
{
    [TestMethod]
    public void BreakpointRequiresAbsoluteRocketSourceAndPositiveLine()
    {
        var source = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "main.rocket"));
        var breakpoint = new RocketDebugBreakpoint(source, 12);

        Assert.AreEqual(source, breakpoint.SourcePath);
        Assert.AreEqual(12, breakpoint.Line);
        Assert.IsFalse(breakpoint.IsBound);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RocketDebugBreakpoint(source, 0));
        Assert.ThrowsExactly<ArgumentException>(() => new RocketDebugBreakpoint("main.rocket", 1));
    }

    [TestMethod]
    public void LaunchRequestNormalizesArtifactPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rocketide-debug-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var request = new RocketDebugLaunchRequest(
                Path.Combine(root, "app.exe"),
                Path.Combine(root, "app.pdb"),
                Path.Combine(root, "app.rocket.map.json"),
                root,
                root,
                ["alpha", "two words"],
                []);

            Assert.IsTrue(Path.IsPathFullyQualified(request.ExecutablePath));
            Assert.IsTrue(Path.IsPathFullyQualified(request.PdbPath));
            Assert.IsTrue(Path.IsPathFullyQualified(request.SourceMapPath));
            Assert.IsTrue(Path.IsPathFullyQualified(request.SourceRoot));
            Assert.IsTrue(Path.IsPathFullyQualified(request.WorkingDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
