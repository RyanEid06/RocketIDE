using System.IO;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class HardeningSourceWiringTests
{
    [TestMethod]
    public void LargeFileEditor_KeepsLocalRocketIndentationIndependentOfLspAndGuardsFeatureReattach()
    {
        var source = ReadSource("src", "RocketIDE.App", "Editor", "EditorDocumentHost.xaml.cs");

        StringAssert.Contains(source, "if (document.AllowLocalEditing)\n            {\n                RocketIndentationStrategy.Configure(Editor);");
        StringAssert.Contains(source, "IsRocketDocument(document) && document.AllowLsp");
        StringAssert.Contains(source, "nameof(DocumentTabViewModel.AllowLsp)");
        StringAssert.Contains(source, "DetachRocketFeatures();");
    }

    [TestMethod]
    public void RecoverySnapshot_UsesSavedDocumentBaselineInsteadOfCurrentDiskFingerprint()
    {
        var source = ReadSource("src", "RocketIDE.App", "MainWindow.Reliability.cs");

        StringAssert.Contains(source, "var baseline = _documentStore.GetRecoveryBaseline(tab.Id);");
        Assert.IsFalse(
            source.Contains("ComputeFingerprintAsync(tab.Path", StringComparison.Ordinal),
            "Periodic recovery must never redefine the saved baseline from whatever happens to be on disk at snapshot time.");
    }

    [TestMethod]
    public void RecoveryRestore_HandlesDeletedSourceAndMarksConflictsForExplicitOverwrite()
    {
        var source = ReadSource("src", "RocketIDE.App", "MainWindow.Reliability.cs");
        var saveSource = ReadSource("src", "RocketIDE.App", "MainWindow.xaml.cs");

        StringAssert.Contains(source, "OpenRecoveredAsync(");
        StringAssert.Contains(source, "tab.MarkRecoveryConflict(");
        StringAssert.Contains(source, "if (!_reliabilityLoaded || _recoveryNeedsDecision)");
        StringAssert.Contains(saveSource, "tab.HasRecoveryConflict");
        StringAssert.Contains(saveSource, "tab.ClearRecoveryConflict();");
        StringAssert.Contains(saveSource, "await SaveRecoverySnapshotAsync();");
        StringAssert.Contains(saveSource, "CloseTabsWithoutPrompt(tabs);\n        await SaveRecoverySnapshotAsync();");
    }

    [TestMethod]
    public void WindowClose_PreservesOpenTabListUntilSessionStateIsSaved()
    {
        var source = ReadSource("src", "RocketIDE.App", "MainWindow.xaml.cs");

        StringAssert.Contains(source, "!await ConfirmDirtyTabsAsync(_viewModel.Documents.ToArray())");
        Assert.IsFalse(
            source.Contains("!await TryCloseTabsAsync(_viewModel.Documents.ToArray())", StringComparison.Ordinal),
            "Window shutdown must not remove every tab before SaveSessionAsync captures the session.");
    }

    [TestMethod]
    public void DebuggerCommandAndFrameNavigationUseActiveRocketDocumentAndAuthoritativeStoppedLocation()
    {
        var viewModel = ReadSource("src", "RocketIDE.App", "ViewModels", "MainWindowViewModel.cs");
        var debuggerSource = ReadSource("src", "RocketIDE.App", "MainWindow.Debugger.cs");

        StringAssert.Contains(viewModel, "ActiveDocument is not null && IsRocketPath(ActiveDocument.Path)");
        var activeDocumentSetter = viewModel[viewModel.IndexOf("public DocumentTabViewModel? ActiveDocument", StringComparison.Ordinal)..viewModel.IndexOf("public string WindowTitle", StringComparison.Ordinal)];
        StringAssert.Contains(activeDocumentSetter, "RaiseRocketCommandProperties();");

        var handlerStart = debuggerSource.IndexOf("private async void DebugFrames_MouseDoubleClick", StringComparison.Ordinal);
        var handlerEnd = debuggerSource.IndexOf("private async Task StartDebugSessionAsync", handlerStart, StringComparison.Ordinal);
        Assert.IsTrue(handlerStart >= 0);
        Assert.IsTrue(handlerEnd > handlerStart);
        var handler = debuggerSource[handlerStart..handlerEnd];
        StringAssert.Contains(handler, "await _nativeDebugger.SelectFrameAsync(frame.Index, CancellationToken.None);");
        Assert.IsFalse(
            handler.Contains("NavigateToDebugLocationAsync", StringComparison.Ordinal),
            "Frame selection navigation must come from the debugger's refreshed Stopped event, not stale pre-selection frame data.");
    }

    private static string ReadSource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RocketIDE.sln")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "Could not locate the RocketIDE repository root from the test output directory.");
        return File.ReadAllText(Path.Combine([directory.FullName, .. segments]));
    }
}
