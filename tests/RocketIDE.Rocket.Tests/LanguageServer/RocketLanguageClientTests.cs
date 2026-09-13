using System.Text.Json;
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

    [TestMethod]
    public void CreateClientCapabilities_AdvertisesTextOnlyTransactionalWorkspaceEditsWithoutResourceOperations()
    {
        var capabilities = JsonSerializer.SerializeToElement(RocketLanguageClient.CreateClientCapabilities());
        var workspaceEdit = capabilities.GetProperty("workspace").GetProperty("workspaceEdit");

        Assert.IsTrue(workspaceEdit.GetProperty("documentChanges").GetBoolean());
        Assert.AreEqual("textOnlyTransactional", workspaceEdit.GetProperty("failureHandling").GetString());
        Assert.IsFalse(workspaceEdit.TryGetProperty("resourceOperations", out _));
    }

    [TestMethod]
    public void CreateClientCapabilities_AdvertisesPublishedDiagnosticDataSupportForCodeActionRoundTrip()
    {
        var capabilities = JsonSerializer.SerializeToElement(RocketLanguageClient.CreateClientCapabilities());
        var publishDiagnostics = capabilities.GetProperty("textDocument").GetProperty("publishDiagnostics");

        Assert.IsTrue(publishDiagnostics.GetProperty("dataSupport").GetBoolean());
    }

    [TestMethod]
    public void CreateClientCapabilities_AdvertisesEditBearingQuickFixCodeActions()
    {
        var capabilities = JsonSerializer.SerializeToElement(RocketLanguageClient.CreateClientCapabilities());
        var codeAction = capabilities.GetProperty("textDocument").GetProperty("codeAction");
        var kinds = codeAction.GetProperty("codeActionLiteralSupport")
            .GetProperty("codeActionKind")
            .GetProperty("valueSet")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();

        CollectionAssert.Contains(kinds, "quickfix");
        Assert.IsTrue(codeAction.GetProperty("disabledSupport").GetBoolean());
    }
}
