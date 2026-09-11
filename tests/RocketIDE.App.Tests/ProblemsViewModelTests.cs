using System.IO;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.Diagnostics;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class ProblemsViewModelTests
{
    [TestMethod]
    public void ApplyPublication_PopulatesAndFiltersProblemItems()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var viewModel = new ProblemsViewModel();
        viewModel.BeginSession(4, isOnline: true);
        viewModel.TrackDocument(path, version: 2, isSupported: true);
        viewModel.ApplyPublication(new RocketDiagnosticPublication(
            4,
            new Uri(path).AbsoluteUri,
            path,
            2,
            [
                Diagnostic(path, "R2001", DiagnosticSeverity.Error, "syntax failure"),
                Diagnostic(path, "R4001", DiagnosticSeverity.Warning, "type warning"),
            ]));

        Assert.AreEqual(2, viewModel.Items.Count);
        Assert.AreEqual("2 problems", viewModel.StatusText);

        viewModel.ShowWarnings = false;
        Assert.AreEqual(1, viewModel.Items.Count);
        Assert.AreEqual("R2001", viewModel.Items[0].Code);

        viewModel.FilterText = "type";
        Assert.AreEqual(0, viewModel.Items.Count);
    }

    [TestMethod]
    public void VersionAdvance_HidesStaleProblemsUntilCurrentZeroDiagnosticsArrive()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var viewModel = new ProblemsViewModel();
        viewModel.BeginSession(8, isOnline: true);
        viewModel.TrackDocument(path, 1, isSupported: true);
        viewModel.ApplyPublication(new RocketDiagnosticPublication(8, new Uri(path).AbsoluteUri, path, 1, [Diagnostic(path, "R2001", DiagnosticSeverity.Error, "bad")]));

        viewModel.TrackDocument(path, 2, isSupported: true);

        Assert.AreEqual(0, viewModel.Items.Count);
        Assert.AreEqual(LiveDiagnosticDocumentState.Stale, viewModel.GetDocumentState(path));
        StringAssert.Contains(viewModel.StatusText, "updating");

        viewModel.ApplyPublication(new RocketDiagnosticPublication(8, new Uri(path).AbsoluteUri, path, 2, []));
        Assert.AreEqual("No problems detected.", viewModel.StatusText);
        Assert.AreEqual(LiveDiagnosticDocumentState.Current, viewModel.GetDocumentState(path));
    }

    [TestMethod]
    public void OfflineAndUnsupportedStatesAreDistinctFromZeroDiagnostics()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var viewModel = new ProblemsViewModel();
        viewModel.BeginSession(1, isOnline: false);
        viewModel.TrackDocument(path, 0, isSupported: true);
        StringAssert.Contains(viewModel.StatusText, "offline");

        viewModel.BeginSession(2, isOnline: true);
        viewModel.TrackDocument(path, 0, isSupported: false);
        StringAssert.Contains(viewModel.StatusText, "4 MiB");

        viewModel.TrackDocument(path, 0, isSupported: true);
        viewModel.ApplyPublication(new RocketDiagnosticPublication(2, new Uri(path).AbsoluteUri, path, 0, []));
        Assert.AreEqual("No problems detected.", viewModel.StatusText);
    }

    [TestMethod]
    public void ProblemItem_ExposesNavigationFileAndOneBasedLocation()
    {
        var path = Path.Combine(Path.GetTempPath(), "main.rocket");
        var diagnostic = new RocketDiagnostic("rocketc", "R4001", "message", DiagnosticSeverity.Warning, path, new SourceRange(4, 6, 4, 9));

        var item = new ProblemItemViewModel(diagnostic);

        Assert.AreEqual(path, item.FilePath);
        Assert.AreEqual(5, item.Line);
        Assert.AreEqual(7, item.Column);
        Assert.AreEqual(new SourceRange(4, 6, 4, 9), item.Range);
    }

    private static RocketDiagnostic Diagnostic(string path, string code, DiagnosticSeverity severity, string message) =>
        new("rocketc", code, message, severity, path, new SourceRange(0, 0, 0, 1));

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-problems-{Guid.NewGuid():N}");
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
