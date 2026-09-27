using System.IO;
using RocketIDE.App.ViewModels;
using RocketIDE.Debugger;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class DebugViewModelTests
{
    [TestMethod]
    public void DisabledBreakpointStaysInPanelButLeavesActiveMarkers()
    {
        var vm = new DebugViewModel();
        var source = Path.GetFullPath("main.rocket");
        vm.ToggleBreakpoint(source, 7);
        vm.SetBreakpointEnabled(vm.Breakpoints.Single(), false);
        Assert.AreEqual("Disabled", vm.Breakpoints.Single().Status);
        Assert.AreEqual(0, vm.GetBreakpointLines(source).Count);
        vm.SetBreakpointEnabled(vm.Breakpoints.Single(), true);
        CollectionAssert.AreEqual(new[] { 7 }, vm.GetBreakpointLines(source).ToArray());
        vm.RemoveBreakpoint(vm.Breakpoints.Single());
        Assert.AreEqual(0, vm.Breakpoints.Count);
    }

    [TestMethod]
    public void WatchesRejectUnsupportedDuplicateAndExcessEntries()
    {
        var vm = new DebugViewModel();
        Assert.IsFalse(vm.TryAddWatch("pair.left", out _));
        for (var i = 0; i < 16; i++) Assert.IsTrue(vm.TryAddWatch("value" + i, out _));
        Assert.IsFalse(vm.TryAddWatch("value0", out _));
        Assert.IsFalse(vm.TryAddWatch("extra", out _));
        Assert.AreEqual(16, vm.Watches.Count);
    }

    [TestMethod]
    public void RunningOrNewFrameInvalidatesLateWatchResults()
    {
        var vm = new DebugViewModel();
        vm.TryAddWatch("value", out _);
        vm.ApplyState(RocketDebugSessionState.Stopped, null);
        var oldRevision = vm.BeginInspection();
        vm.ApplyState(RocketDebugSessionState.Running, null);
        Assert.IsFalse(vm.ApplyWatchResults(oldRevision, [new("value", "int64 0n21", true)]));
        vm.ApplyState(RocketDebugSessionState.Stopped, null);
        var freshRevision = vm.BeginInspection();
        Assert.IsFalse(vm.ApplyWatchResults(oldRevision, [new("value", "stale", true)]));
        Assert.IsTrue(vm.ApplyWatchResults(freshRevision, [new("value", "int64 0n42", true)]));
        vm.EndInspection(freshRevision);
        Assert.AreEqual("int64 0n42", vm.Watches.Single().Value);
        Assert.IsFalse(vm.IsInspecting);
    }

    [TestMethod]
    public void EverySessionStateHasConsistentDebuggerCommands()
    {
        var registry = new RocketIDE.App.Commands.RocketCommandRegistry();
        foreach (var state in Enum.GetValues<RocketDebugSessionState>())
        {
            var vm = new DebugViewModel();
            vm.ApplyState(state, null);
            var context = new RocketIDE.App.Commands.RocketCommandContext(true, true, true, false,
                IsDebugging: vm.IsActive, IsDebuggerRunning: vm.IsRunning, IsDebuggerStopped: vm.IsStopped,
                HasRocketDocument: true, IsDebuggerTerminating: state == RocketDebugSessionState.Terminating,
                IsDebuggerLaunching: state == RocketDebugSessionState.Launching, HasDebugLaunch: true);
            Assert.AreEqual(state == RocketDebugSessionState.Stopped, registry.Evaluate("debug.runToCursor", context).IsEnabled, state.ToString());
            Assert.AreEqual(state == RocketDebugSessionState.Running, registry.Evaluate("debug.pause", context).IsEnabled, state.ToString());
            Assert.AreEqual(state is RocketDebugSessionState.Idle or RocketDebugSessionState.Stopped or RocketDebugSessionState.Terminated or RocketDebugSessionState.Faulted,
                registry.Evaluate("debug.toggleBreakpoint", context).IsEnabled, state.ToString());
            Assert.AreEqual(state is RocketDebugSessionState.Launching or RocketDebugSessionState.Running or RocketDebugSessionState.Stopped,
                registry.Evaluate("debug.stop", context).IsEnabled, state.ToString());
        }
    }

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
