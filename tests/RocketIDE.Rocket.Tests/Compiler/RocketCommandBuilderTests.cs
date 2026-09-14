using RocketIDE.Rocket.Compiler;
using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Tests.Compiler;

[TestClass]
public sealed class RocketCommandBuilderTests
{
    [TestMethod]
    public void Build_PackageCheckBuildAndTestUseExplicitPackagePathAndJsonMessages()
    {
        var compiler = Path.GetFullPath(Path.Combine("sdk", "rocketc.exe"));
        var root = Path.GetFullPath(Path.Combine("work", "demo"));
        var target = new RocketTarget(Path.Combine(root, "src", "main.rocket"), root, Path.Combine(root, "rocket.toml"), false, "executable", "demo");

        foreach (var kind in new[] { RocketCommandKind.Check, RocketCommandKind.Build, RocketCommandKind.Test })
        {
            var spec = RocketCommandBuilder.Build(compiler, kind, target, []);

            Assert.AreEqual(Path.GetDirectoryName(compiler), spec.Request.WorkingDirectory);
            Assert.AreEqual(kind.ToString().ToLowerInvariant(), spec.Request.Arguments[0]);
            Assert.AreEqual(root, spec.Request.Arguments[1]);
            Assert.AreEqual("--message-format=json", spec.Request.Arguments[2]);
            Assert.IsTrue(spec.UsesStructuredMessages);
        }
    }

    [TestMethod]
    public void Build_StandaloneUsesSourcePath()
    {
        var compiler = Path.GetFullPath(Path.Combine("sdk", "rocketc.exe"));
        var source = Path.GetFullPath(Path.Combine("scratch", "hello.rocket"));
        var target = new RocketTarget(source, Path.GetDirectoryName(source)!, null, true);

        var spec = RocketCommandBuilder.Build(compiler, RocketCommandKind.Build, target, []);

        CollectionAssert.AreEqual(new[] { "build", source, "--message-format=json" }, spec.Request.Arguments.ToArray());
    }

    [TestMethod]
    public void Build_DebugBuildUsesRocketDebugFlagAndStructuredMessages()
    {
        var compiler = Path.GetFullPath(Path.Combine("sdk", "rocketc.exe"));
        var source = Path.GetFullPath(Path.Combine("scratch", "hello.rocket"));
        var target = new RocketTarget(source, Path.GetDirectoryName(source)!, null, true);

        var spec = RocketCommandBuilder.Build(compiler, RocketCommandKind.DebugBuild, target, []);

        CollectionAssert.AreEqual(new[] { "build", source, "--debug", "--message-format=json" }, spec.Request.Arguments.ToArray());
        Assert.IsTrue(spec.UsesStructuredMessages);
    }

    [TestMethod]
    public void Build_DebugBuildRejectsLibraryTarget()
    {
        var compiler = Path.GetFullPath(Path.Combine("sdk", "rocketc.exe"));
        var root = Path.GetFullPath(Path.Combine("work", "library"));
        var target = new RocketTarget(Path.Combine(root, "src", "lib.rocket"), root, Path.Combine(root, "rocket.toml"), false, "static-library", "library");

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => RocketCommandBuilder.Build(compiler, RocketCommandKind.DebugBuild, target, []));
        StringAssert.Contains(exception.Message, "Debugging is unavailable");
    }

    [TestMethod]
    public void Build_RunUsesProgramArgumentSeparator()
    {
        var compiler = Path.GetFullPath(Path.Combine("sdk", "rocketc.exe"));
        var source = Path.GetFullPath(Path.Combine("scratch", "hello.rocket"));
        var target = new RocketTarget(source, Path.GetDirectoryName(source)!, null, true);

        var spec = RocketCommandBuilder.Build(compiler, RocketCommandKind.Run, target, ["one", "two words"]);

        CollectionAssert.AreEqual(new[] { "run", source, "--", "one", "two words" }, spec.Request.Arguments.ToArray());
        Assert.IsFalse(spec.UsesStructuredMessages);
    }

    [TestMethod]
    public void Build_RunRejectsLibraryTarget()
    {
        var compiler = Path.GetFullPath(Path.Combine("sdk", "rocketc.exe"));
        var root = Path.GetFullPath(Path.Combine("work", "library"));
        var target = new RocketTarget(Path.Combine(root, "src", "lib.rocket"), root, Path.Combine(root, "rocket.toml"), false, "static-library", "library");

        Assert.ThrowsExactly<InvalidOperationException>(() => RocketCommandBuilder.Build(compiler, RocketCommandKind.Run, target, []));
    }

    [TestMethod]
    public void Build_UsesTrustedCompilerDirectoryInsteadOfWorkspaceAsNativeWorkingDirectory()
    {
        var compiler = Path.GetFullPath(Path.Combine("trusted-sdk", "bin", "rocketc.exe"));
        var workspace = Path.GetFullPath(Path.Combine("untrusted", "workspace"));
        var target = new RocketTarget(Path.Combine(workspace, "src", "main.rocket"), workspace, Path.Combine(workspace, "rocket.toml"), false);

        var spec = RocketCommandBuilder.Build(compiler, RocketCommandKind.Build, target, []);

        Assert.AreEqual(Path.GetDirectoryName(compiler), spec.Request.WorkingDirectory);
        Assert.AreNotEqual(workspace, spec.Request.WorkingDirectory);
        Assert.AreEqual(workspace, spec.Request.Arguments[1]);
    }

}
