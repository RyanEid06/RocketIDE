using System.IO;
using RocketIDE.App.Integration;

namespace RocketIDE.App.Tests.Integration;

[TestClass]
public sealed class DocumentChangeSchedulerTests
{
    [TestMethod]
    public async Task Schedule_CoalescesRapidVersionsToLatestDocument()
    {
        var calls = new List<RocketSessionDocument>();
        await using var scheduler = new DocumentChangeScheduler(async (document, _) =>
        {
            lock (calls)
            {
                calls.Add(document);
            }
            await Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(10));

        scheduler.Schedule(new RocketSessionDocument("main.rocket", "v1", 1, "main"));
        scheduler.Schedule(new RocketSessionDocument("main.rocket", "v2", 2, "main"));
        await scheduler.WaitForIdleAsync();

        Assert.AreEqual(1, calls.Count);
        Assert.AreEqual(2, calls[0].Version);
        Assert.AreEqual("v2", calls[0].Text);
    }

    [TestMethod]
    public async Task Cancel_PreventsPendingDocumentChange()
    {
        var called = false;
        await using var scheduler = new DocumentChangeScheduler((_, _) =>
        {
            called = true;
            return Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(50));

        scheduler.Schedule(new RocketSessionDocument("main.rocket", "v1", 1, "main"));
        scheduler.Cancel("main.rocket");
        await scheduler.WaitForIdleAsync();

        Assert.IsFalse(called);
    }

    [TestMethod]
    public async Task SaveAsync_FlushesLatestChangeBeforePersistAndMatchingDidSave()
    {
        var events = new List<string>();
        await using var scheduler = new DocumentChangeScheduler((document, _) =>
        {
            events.Add($"change:{document.Version}:{document.Text}");
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(1));

        scheduler.Schedule(new RocketSessionDocument("main.rocket", "old", 1, "main"));
        scheduler.Schedule(new RocketSessionDocument("main.rocket", "new", 2, "main"));
        await scheduler.SaveAsync(
            new RocketSessionDocument("main.rocket", "new", 2, "main"),
            (_, _) =>
            {
                events.Add("persist:2:new");
                return Task.FromResult<RocketSessionDocument?>(new("main.rocket", "new", 2, "main"));
            },
            (document, _) =>
            {
                events.Add($"save:{document.Version}:{document.Text}");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "change:2:new", "persist:2:new", "save:2:new" }, events);
    }

    [TestMethod]
    public async Task SaveAsync_WaitsForInFlightChangeAndDoesNotSendSaveForCancelledPersistence()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        await using var scheduler = new DocumentChangeScheduler(async (document, _) =>
        {
            events.Add($"change:{document.Version}");
            started.TrySetResult();
            await release.Task;
        }, TimeSpan.Zero);

        scheduler.Schedule(new RocketSessionDocument("main.rocket", "old", 1, "main"));
        await started.Task;
        var save = scheduler.SaveAsync(
            new RocketSessionDocument("main.rocket", "new", 2, "main"),
            (_, _) =>
            {
                events.Add("persist");
                return Task.FromResult<RocketSessionDocument?>(null);
            },
            (_, _) =>
            {
                events.Add("save");
                return Task.CompletedTask;
            },
            CancellationToken.None);
        Assert.IsFalse(save.IsCompleted);
        release.TrySetResult();
        await save;

        CollectionAssert.AreEqual(new[] { "change:1", "change:2", "persist" }, events);
    }

    [TestMethod]
    public async Task SaveAsync_PersistsWhenLspSynchronizationFails()
    {
        var events = new List<string>();
        await using var scheduler = new DocumentChangeScheduler((_, _) => throw new IOException("LSP offline"),
            onError: _ => events.Add("degraded"));

        var saved = await scheduler.SaveAsync(
            new RocketSessionDocument("main.rocket", "new", 2, "main"),
            (document, _) => { events.Add("persist"); return Task.FromResult<RocketSessionDocument?>(document); },
            (_, _) => { events.Add("didSave"); return Task.CompletedTask; }, CancellationToken.None);

        Assert.IsNotNull(saved);
        CollectionAssert.AreEqual(new[] { "degraded", "persist", "degraded" }, events);
    }
}
