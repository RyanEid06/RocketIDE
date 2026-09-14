using System.IO;
using RocketIDE.App.ViewModels;
using RocketIDE.Debugger;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class DebugViewModelTests
{
    [TestMethod]
    public void ToggleBreakpointAddsAndRemovesSameSourceLine()
    {
        var viewModel = new DebugViewModel();
        var source = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "main.rocket"));

        viewModel.ToggleBreakpoint(source, 7);
        Assert.AreEqual(1, viewModel.Breakpoints.Count);

        viewModel.ToggleBreakpoint(source, 7);
        Assert.AreEqual(0, viewModel.Breakpoints.Count);
    }

    [TestMethod]
    public void ApplyStateExposesDebuggerCommandStateFlags()
    {
        var viewModel = new DebugViewModel();

        viewModel.ApplyState(RocketDebugSessionState.Running, "running");
        Assert.IsTrue(viewModel.IsActive);
        Assert.IsTrue(viewModel.IsRunning);
        Assert.IsFalse(viewModel.IsStopped);

        viewModel.ApplyState(RocketDebugSessionState.Stopped, "stopped");
        Assert.IsTrue(viewModel.IsStopped);
    }
}
