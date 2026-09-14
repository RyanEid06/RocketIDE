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
    }
}
