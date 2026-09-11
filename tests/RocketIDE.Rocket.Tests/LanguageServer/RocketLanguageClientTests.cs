using RocketIDE.Rocket.LanguageServer;

namespace RocketIDE.Rocket.Tests.LanguageServer;

[TestClass]
public sealed class RocketLanguageClientTests
{
    [TestMethod]
    public void CreateProcessStartInfo_UsesTrustedToolDirectoryInsteadOfWorkspaceControlledDirectory()
    {
        var serverPath = Path.Combine(Path.GetTempPath(), "trusted-sdk", "rocket-lsp.exe");

        var startInfo = RocketLanguageClient.CreateProcessStartInfo(serverPath);

        Assert.AreEqual(Path.GetFullPath(serverPath), startInfo.FileName);
        Assert.AreEqual(Path.GetDirectoryName(Path.GetFullPath(serverPath)), startInfo.WorkingDirectory);
        Assert.IsFalse(startInfo.UseShellExecute);
        Assert.IsTrue(startInfo.CreateNoWindow);
        Assert.IsTrue(startInfo.RedirectStandardInput);
        Assert.IsTrue(startInfo.RedirectStandardOutput);
        Assert.IsTrue(startInfo.RedirectStandardError);
    }
}
