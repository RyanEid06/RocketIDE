using RocketIDE.App.Commands;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class CommandRegistryTests
{
    [TestMethod]
    public void BuildAndRunStateRespectTargetBusyAndRunCapability()
    {
        var registry = new RocketCommandRegistry();

        Assert.IsTrue(registry.Evaluate(RocketCommandRegistry.Build, new(true, true, false, false)).IsEnabled);
        Assert.IsFalse(registry.Evaluate(RocketCommandRegistry.Run, new(true, true, false, false)).IsEnabled);
        Assert.IsFalse(registry.Evaluate(RocketCommandRegistry.Build, new(true, true, true, true)).IsEnabled);
        Assert.IsTrue(registry.Evaluate(RocketCommandRegistry.Stop, new(true, true, true, true)).IsEnabled);
    }
    [TestMethod]
    public void ProductionCommandsUseCentralizedAvailabilityAndGestures()
    {
        var registry = new RocketCommandRegistry();
        var ready = new RocketCommandContext(true, true, true, false, true);
        var busy = ready with { IsBusy = true };
        var noTarget = ready with { HasTarget = false, CanRun = false };

        Assert.AreEqual("Ctrl+Shift+F5", registry.Get(RocketCommandRegistry.Stop).Gesture);
        Assert.AreEqual("Ctrl+P", registry.Get(RocketCommandRegistry.QuickOpen).Gesture);
        Assert.AreEqual("Ctrl+Shift+M", registry.Get(RocketCommandRegistry.Problems).Gesture);
        Assert.AreEqual("Ctrl+Shift+U", registry.Get(RocketCommandRegistry.Output).Gesture);
        Assert.AreEqual(RocketCommandRegistry.Build, registry.FindByGesture("ctrl+b")?.Id);
        Assert.IsTrue(registry.Evaluate(RocketCommandRegistry.Resolve, ready).IsEnabled);
        Assert.IsFalse(registry.Evaluate(RocketCommandRegistry.Resolve, busy).IsEnabled);
        Assert.IsFalse(registry.Evaluate(RocketCommandRegistry.Resolve, noTarget).IsEnabled);
        Assert.IsTrue(registry.Evaluate(RocketCommandRegistry.NewProject, ready).IsEnabled);
        Assert.IsFalse(registry.Evaluate(RocketCommandRegistry.NewProject, ready with { HasWorkspace = false }).IsEnabled);
    }

}
