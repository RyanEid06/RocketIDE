using RocketIDE.App.ViewModels;
using RocketIDE.Rocket.Compiler;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class TestsViewModelTests
{
    [TestMethod]
    public void StructuredTestEventsPopulateRowsAndSummary()
    {
        var viewModel = new TestsViewModel();
        viewModel.BeginRun();

        viewModel.Apply(new RocketMessage("test-started", Name: "alpha"));
        viewModel.Apply(new RocketMessage("test-finished", Name: "alpha", Status: "pass", ExitCode: 0));
        viewModel.Apply(new RocketMessage("test-started", Name: "beta"));
        viewModel.Apply(new RocketMessage("test-finished", Name: "beta", Status: "fail", ExitCode: 3));
        viewModel.Apply(new RocketMessage("test-summary", Passed: 1, Failed: 1, ExpectedFailures: 0, Selected: 2));

        Assert.AreEqual(2, viewModel.Items.Count);
        Assert.AreEqual("PASS", viewModel.Items[0].Status);
        Assert.AreEqual("FAIL", viewModel.Items[1].Status);
        Assert.AreEqual(3, viewModel.Items[1].ExitCode);
        Assert.AreEqual("1 passed · 1 failed · 0 expected failure(s) · 2 selected", viewModel.SummaryText);
        Assert.AreEqual("TESTS (2)", viewModel.HeaderText);
    }

    [TestMethod]
    public void BeginRunClearsPriorResults()
    {
        var viewModel = new TestsViewModel();
        viewModel.Apply(new RocketMessage("test-finished", Name: "old", Status: "pass", ExitCode: 0));

        viewModel.BeginRun();

        Assert.AreEqual(0, viewModel.Items.Count);
        Assert.AreEqual("Test run started…", viewModel.SummaryText);
    }
}
