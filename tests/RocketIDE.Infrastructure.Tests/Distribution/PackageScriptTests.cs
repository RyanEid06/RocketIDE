namespace RocketIDE.Infrastructure.Tests.Distribution;

[TestClass]
public sealed class PackageScriptTests
{
    [TestMethod]
    public void PackageScriptDeclaresSelfContainedWinX64AndChecksum()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "package.ps1"));
        if (!File.Exists(path))
        {
            Assert.Inconclusive($"Package script is not visible from this test output: {path}");
        }

        var script = File.ReadAllText(path);
        StringAssert.Contains(script, "win-x64");
        StringAssert.Contains(script, "--self-contained");
        StringAssert.Contains(script, "Get-FileHash");
        StringAssert.Contains(script, "Compress-Archive");
        StringAssert.Contains(script, "$publishRoot = $packageRoot");
        Assert.IsFalse(script.Contains("Join-Path $packageRoot 'app'", StringComparison.Ordinal),
            "The portable entry point must not be nested under an app subdirectory.");
        StringAssert.Contains(script, "Join-Path $packageRoot 'RocketIDE.exe'");

        Assert.IsFalse(script.Contains("PublishSingleFile=true", StringComparison.Ordinal),
            "DbgX requires EngHost.exe to remain a real child process in the portable package.");
        StringAssert.Contains(script, "RocketIDE.Debugger.dll");
        StringAssert.Contains(script, "EngHost.exe");
    }

    [TestMethod]
    public void VerifyScriptPublishesDebuggerEngineAsMultiFileAssets()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "verify.ps1"));
        if (!File.Exists(path))
        {
            Assert.Inconclusive($"Verify script is not visible from this test output: {path}");
        }

        var script = File.ReadAllText(path);
        Assert.IsFalse(script.Contains("PublishSingleFile=true", StringComparison.Ordinal));
        StringAssert.Contains(script, "RocketIDE.Debugger.dll");
        StringAssert.Contains(script, "EngHost.exe");
        StringAssert.Contains(script, "x64");
    }


    [TestMethod]
    public void DebuggerDependencyGraphPinsServicedFrameworkPackages()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var propsPath = Path.Combine(root, "Directory.Packages.props");
        var debuggerProjectPath = Path.Combine(root, "src", "RocketIDE.Debugger", "RocketIDE.Debugger.csproj");

        if (!File.Exists(propsPath) || !File.Exists(debuggerProjectPath))
        {
            Assert.Inconclusive("Debugger dependency source files are not visible from this test output.");
        }

        var props = File.ReadAllText(propsPath);
        StringAssert.Contains(props, """<PackageVersion Include="System.ComponentModel.Composition" Version="10.0.12" />""");
        StringAssert.Contains(props, """<PackageVersion Include="System.Security.Cryptography.Xml" Version="10.0.12" />""");

        var project = File.ReadAllText(debuggerProjectPath);
        StringAssert.Contains(project, """<PackageReference Include="System.ComponentModel.Composition" />""");
        StringAssert.Contains(project, """<PackageReference Include="System.Security.Cryptography.Xml" />""");
    }
}
