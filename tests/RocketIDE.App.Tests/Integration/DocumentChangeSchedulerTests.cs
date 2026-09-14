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
}
