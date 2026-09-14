using RocketIDE.Rocket.Compiler;
using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Tests.Compiler;

[TestClass]
public sealed class RocketAdvancedCommandTests
{
    [TestMethod]
    public void BuildResolve_AddsLockedAndOfflineFlagsAfterPackageTarget()
    {
        var target = PackageTarget();

        var command = RocketCommandCatalog.Build(
            CompilerPath(),
            RocketAdvancedCommandKind.Resolve,
            target,
            new RocketAdvancedCommandOptions(Locked: true, Offline: true));

        CollectionAssert.AreEqual(
            new[] { "resolve", target.WorkingDirectory, "--locked", "--offline" },
            command.Request.Arguments.ToArray());
        Assert.IsFalse(command.MayExecuteNativeCode);
    }

    [TestMethod]
    public void BuildFormat_AddsCheckOnlyFlagAndUsesStructuredOutputWhenRequested()
    {
        var command = RocketCommandCatalog.Build(
            CompilerPath(),
            RocketAdvancedCommandKind.Format,
            PackageTarget(),
            new RocketAdvancedCommandOptions(CheckOnly: true));

        CollectionAssert.AreEqual(
            new[] { "fmt", PackageTarget().WorkingDirectory, "--check" },
            command.Request.Arguments.ToArray());
        Assert.IsFalse(command.MayExecuteNativeCode);
    }

    [TestMethod]
    public void BuildNew_RejectsNonEmptyDestinationWithoutExplicitConfirmation()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "existing.txt"), "content");

        Assert.ThrowsExactly<InvalidOperationException>(() => RocketCommandCatalog.Build(
            CompilerPath(),
            RocketAdvancedCommandKind.New,
            target: null,
            options: new RocketAdvancedCommandOptions(DestinationPath: temp.Path)));
    }

    [TestMethod]
    public void BuildCoverage_QuotesOutputPathAndMarksNativeExecutionRisk()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "reports", "coverage report");

        var command = RocketCommandCatalog.Build(
            CompilerPath(),
            RocketAdvancedCommandKind.Coverage,
            PackageTarget(),
            new RocketAdvancedCommandOptions(OutputPath: output));

        CollectionAssert.AreEqual(
            new[] { "coverage", PackageTarget().WorkingDirectory, "--output", output },
            command.Request.Arguments.ToArray());
        Assert.IsTrue(command.MayExecuteNativeCode);
        Assert.AreEqual(Path.GetFullPath(output), command.OutputPath);
    }

    private static RocketTarget PackageTarget() => new(
        Path.Combine(Path.GetTempPath(), "rocketide-package"),
        Path.Combine(Path.GetTempPath(), "rocketide-package"),
        Path.Combine(Path.GetTempPath(), "rocketide-package", "rocket.toml"),
        IsStandalone: false);

    private static string CompilerPath() => Path.Combine(Path.GetTempPath(), "rocketc.exe");

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-advanced-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
