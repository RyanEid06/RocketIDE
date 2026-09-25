using System.Diagnostics;
using RocketIDE.App.Integration;

namespace RocketIDE.App.Tests.Integration;

[TestClass]
public sealed class ApplicationLifetimeCoordinatorTests
{
    [TestMethod]
    public async Task Shutdown_UsesOneMonotonicBudgetAcrossHungDebuggerLspAndPersistence()
    {
        using var lifetime = new ApplicationLifetimeCoordinator(
            TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(150));
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var elapsed = Stopwatch.StartNew();

        Assert.IsTrue(lifetime.TryAcceptWork());
        lifetime.BeginShutdown();
        Assert.IsFalse(lifetime.TryAcceptWork());
        Assert.IsTrue(lifetime.WorkToken.IsCancellationRequested);
        Assert.IsFalse(await lifetime.RunGracefulAsync("debugger", _ => never.Task));
        Assert.IsFalse(await lifetime.RunGracefulAsync("LSP", _ => never.Task));
        Assert.IsFalse(await lifetime.RunForcedAsync("persistence", _ => never.Task));

        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromMilliseconds(600), $"Shutdown took {elapsed.Elapsed}.");
    }

    [TestMethod]
    public async Task Shutdown_RunsFastCleanupWithinSharedDeadline()
    {
        using var lifetime = new ApplicationLifetimeCoordinator(
            TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400));
        var order = new List<string>();
        lifetime.BeginShutdown();

        Assert.IsTrue(await lifetime.RunGracefulAsync("debugger", _ => { order.Add("debugger"); return Task.CompletedTask; }));
        Assert.IsTrue(await lifetime.RunGracefulAsync("LSP", _ => { order.Add("LSP"); return Task.CompletedTask; }));
        Assert.IsTrue(await lifetime.RunForcedAsync("persistence", _ => { order.Add("persistence"); return Task.CompletedTask; }));

        CollectionAssert.AreEqual(new[] { "debugger", "LSP", "persistence" }, order);
    }

    [TestMethod]
    public async Task Shutdown_BoundsDirtySaveAndSubsequentTeardownWithOneClock()
    {
        using var lifetime = new ApplicationLifetimeCoordinator(
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(100));
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var elapsed = Stopwatch.StartNew();
        lifetime.BeginShutdown();

        Assert.IsFalse(await lifetime.RunGracefulAsync("dirty save", _ => never.Task));
        Assert.IsFalse(await lifetime.RunGracefulAsync("debugger", _ => never.Task));
        Assert.IsFalse(await lifetime.RunForcedAsync("persistence", _ => never.Task));
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromMilliseconds(350), $"Shutdown took {elapsed.Elapsed}.");
    }
}
