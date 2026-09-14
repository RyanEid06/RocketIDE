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
}
