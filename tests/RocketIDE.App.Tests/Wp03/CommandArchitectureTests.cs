using RocketIDE.App.Commands;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Tests.Wp03;

[TestClass]
public sealed class CommandArchitectureTests
{
    [TestMethod]
    public async Task Dispatcher_UsesRegistryStateAndDoesNotRunDisabledHandler()
    {
        var registry = new RocketCommandRegistry();
        var enabled = false;
        var calls = 0;
        var dispatcher = new RocketCommandDispatcher(
            registry,
            id => new RocketCommandState(id, enabled, false));
        dispatcher.Register(RocketCommandRegistry.Build, _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        Assert.IsFalse(await dispatcher.ExecuteAsync(RocketCommandRegistry.Build));
        Assert.AreEqual(0, calls);
        enabled = true;
        Assert.IsTrue(await dispatcher.ExecuteAsync(RocketCommandRegistry.Build));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void Palette_FuzzyFiltersShowsShortcutAndPreservesDisabledState()
    {
        var registry = new RocketCommandRegistry();
        var viewModel = new CommandPaletteViewModel(
            registry,
            id => new RocketCommandState(id, id != RocketCommandRegistry.Build, false));

        viewModel.Query = "build";

        var build = viewModel.Items.Single(item => item.Definition.Id == RocketCommandRegistry.Build);
        Assert.AreEqual("Ctrl+B", build.Gesture);
        Assert.IsFalse(build.IsEnabled);
    }

    [TestMethod]
    public void Registry_HasNoDuplicateNonEmptyGestures()
    {
        var gestures = new RocketCommandRegistry().Definitions
            .Where(item => !string.IsNullOrWhiteSpace(item.Gesture))
            .GroupBy(item => item.Gesture, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();
        Assert.AreEqual(0, gestures.Length);
    }
}
