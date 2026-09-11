using RocketIDE.Rocket.Tools;

namespace RocketIDE.Rocket.Tests.Tools;

[TestClass]
public sealed class ProcessRocketToolVersionProbeTests
{
    [TestMethod]
    public void CreateProcessStartInfo_UsesExecutableDirectoryAsNativeWorkingDirectory()
    {
        var executable = Path.Combine(Path.GetTempPath(), "trusted-sdk", "rocketc.exe");

        var startInfo = ProcessRocketToolVersionProbe.CreateProcessStartInfo(executable);

        Assert.AreEqual(Path.GetFullPath(executable), startInfo.FileName);
        Assert.AreEqual(Path.GetDirectoryName(Path.GetFullPath(executable)), startInfo.WorkingDirectory);
        Assert.IsFalse(startInfo.UseShellExecute);
        Assert.IsTrue(startInfo.CreateNoWindow);
        Assert.IsTrue(startInfo.RedirectStandardOutput);
        Assert.IsTrue(startInfo.RedirectStandardError);
        CollectionAssert.AreEqual(new[] { "--version" }, startInfo.ArgumentList.ToArray());
    }
}
