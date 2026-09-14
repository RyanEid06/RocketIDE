using RocketIDE.Rocket.Debugger;
using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Tests.Debugger;

[TestClass]
public sealed class RocketDebugArtifactPathsTests
{
    [TestMethod]
    public void FromCompilerArtifactUsesReportedExecutableAndAdjacentDebugArtifacts()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"rocketide-artifacts-{Guid.NewGuid():N}"));
        var target = new RocketTarget(Path.Combine(root, "src", "main.rocket"), root, Path.Combine(root, "rocket.toml"), false, "executable", "demo");

        var paths = RocketDebugArtifactPaths.FromCompilerArtifact(target, Path.Combine(".rocketc", "demo.exe"));

        Assert.AreEqual(Path.Combine(root, ".rocketc", "demo.exe"), paths.ExecutablePath);
        Assert.AreEqual(Path.Combine(root, ".rocketc", "demo.pdb"), paths.PdbPath);
        Assert.AreEqual(Path.Combine(root, ".rocketc", "demo.rocket.map.json"), paths.SourceMapPath);
        Assert.AreEqual(root, paths.SourceRoot);
        Assert.AreEqual(root, paths.WorkingDirectory);
    }

    [TestMethod]
    public void FromCompilerArtifactRejectsNonExecutableTargetAndArtifact()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rocketide-artifacts"));
        var library = new RocketTarget(Path.Combine(root, "src", "lib.rocket"), root, null, false, "static-library", "lib");
        var executable = new RocketTarget(Path.Combine(root, "src", "main.rocket"), root, null, false, "executable", "demo");

        Assert.ThrowsExactly<InvalidOperationException>(() => RocketDebugArtifactPaths.FromCompilerArtifact(library, "lib.lib"));
        Assert.ThrowsExactly<InvalidOperationException>(() => RocketDebugArtifactPaths.FromCompilerArtifact(executable, "demo.obj"));
    }
}
