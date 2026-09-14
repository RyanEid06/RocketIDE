using RocketIDE.App.ViewModels;
using RocketIDE.Infrastructure.Files;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class RocketCommandStateTests
{
    [TestMethod]
    public void CommandStatePreventsConcurrentStartsAndDisablesRunForLibrary()
    {
        var viewModel = new MainWindowViewModel(new WorkspaceFileSystem());
        viewModel.SetRocketCommandAvailability(hasTarget: true, canRun: false);

        Assert.IsTrue(viewModel.CanRocketCheck);
        Assert.IsTrue(viewModel.CanRocketBuild);
        Assert.IsTrue(viewModel.CanRocketTest);
        Assert.IsFalse(viewModel.CanRocketRun);
        Assert.IsFalse(viewModel.CanRocketStop);

        viewModel.SetRocketCommandRunning(true);

        Assert.IsFalse(viewModel.CanRocketCheck);
        Assert.IsFalse(viewModel.CanRocketBuild);
        Assert.IsFalse(viewModel.CanRocketTest);
        Assert.IsFalse(viewModel.CanRocketRun);
        Assert.IsTrue(viewModel.CanRocketStop);
    }
}
