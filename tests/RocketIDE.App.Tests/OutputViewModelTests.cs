using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class OutputViewModelTests
{
    [TestMethod]
    public void Append_PreservesLinesAndCapsHistory()
    {
        var viewModel = new OutputViewModel(maxLines: 3);

        viewModel.Append("one");
        viewModel.Append("two");
        viewModel.Append("three");
        viewModel.Append("four");

        CollectionAssert.AreEqual(new[] { "two", "three", "four" }, viewModel.Lines.ToArray());
    }

    [TestMethod]
    public void BeginCommand_AddsVisibleBoundaryWithoutClearingExistingHistory()
    {
        var viewModel = new OutputViewModel();
        viewModel.Append("old output");

        viewModel.BeginCommand("Build", "C:\\work\\demo");

        Assert.AreEqual("old output", viewModel.Lines[0]);
        StringAssert.Contains(viewModel.Lines[^1], "Build");
        StringAssert.Contains(viewModel.Lines[^1], "C:\\work\\demo");
    }
}
