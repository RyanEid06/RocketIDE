using System.IO;
using RocketIDE.App.Integration;

namespace RocketIDE.App.Tests.Integration;

[TestClass]
public sealed class DocumentChangeSchedulerWp04Tests
{
    [TestMethod]
    public async Task SaveAsync_PrepareStageRunsAfterInitialSyncAndSynchronizesPreparedVersionBeforePersist()
    {
        var events = new List<string>();
        await using var scheduler = new DocumentChangeScheduler((document, _) =>
        {
            events.Add($"change:{document.Version}:{document.Text}");
            return Task.CompletedTask;
        });

        var saved = await scheduler.SaveAsync(
            new RocketSessionDocument("main.rocket", "raw", 2, "main"),
            (document, _) =>
            {
                events.Add($"prepare:{document.Version}:{document.Text}");
                return Task.FromResult(new RocketSessionDocument(document.Path, "formatted", 3, document.DisplayName));
            },
            (document, _) =>
            {
                events.Add($"persist:{document.Version}:{document.Text}");
                return Task.FromResult<RocketSessionDocument?>(document);
            },
            (document, _) =>
            {
                events.Add($"save:{document.Version}:{document.Text}");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.IsNotNull(saved);
        CollectionAssert.AreEqual(
            new[]
            {
                "change:2:raw",
                "prepare:2:raw",
                "change:3:formatted",
                "persist:3:formatted",
                "save:3:formatted",
            },
            events);
    }

    [TestMethod]
    public async Task SaveAsync_WhenInitialSyncFails_SkipsPreparationAndStillPersistsUnformattedContents()
    {
        var events = new List<string>();
        await using var scheduler = new DocumentChangeScheduler(
            (_, _) => throw new IOException("offline"),
            onError: _ => events.Add("degraded"));
        var prepareCalls = 0;

        var saved = await scheduler.SaveAsync(
            new RocketSessionDocument("main.rocket", "raw", 2, "main"),
            (document, _) =>
            {
                prepareCalls++;
                return Task.FromResult(document with { Text = "formatted", Version = 3 });
            },
            (document, _) =>
            {
                events.Add($"persist:{document.Text}");
                return Task.FromResult<RocketSessionDocument?>(document);
            },
            (_, _) => { events.Add("didSave"); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.IsNotNull(saved);
        Assert.AreEqual(0, prepareCalls);
        Assert.AreEqual("raw", saved.Text);
        CollectionAssert.AreEqual(new[] { "degraded", "persist:raw", "degraded" }, events);
    }

    [TestMethod]
    public async Task SaveAsync_FormatterFailureFallsBackToPersistingSynchronizedUnformattedText()
    {
        var events = new List<string>();
        await using var scheduler = new DocumentChangeScheduler((document, _) =>
        {
            events.Add($"change:{document.Version}:{document.Text}");
            return Task.CompletedTask;
        });
        var format = new FormatOnSaveService(message => events.Add($"output:{message}"));
        var original = new RocketSessionDocument("main.rocket", "raw", 2, "main");

        var saved = await scheduler.SaveAsync(
            original,
            (document, token) => format.PrepareAsync(
                document,
                () => document,
                _ => throw new IOException("formatter unavailable"),
                (_, _) => Task.CompletedTask,
                token),
            (document, _) =>
            {
                events.Add($"persist:{document.Version}:{document.Text}");
                return Task.FromResult<RocketSessionDocument?>(document);
            },
            (document, _) =>
            {
                events.Add($"save:{document.Version}:{document.Text}");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.AreEqual(original, saved);
        Assert.IsTrue(events.Any(item => item.Contains("formatter unavailable", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(
            new[] { "change:2:raw", "persist:2:raw", "save:2:raw" },
            events.Where(item => !item.StartsWith("output:", StringComparison.Ordinal)).ToArray());
    }

    [TestMethod]
    public async Task SaveAsync_PrepareCancellationDoesNotPersistOrSendDidSave()
    {
        var events = new List<string>();
        await using var scheduler = new DocumentChangeScheduler((_, _) => Task.CompletedTask);
        using var cancellation = new CancellationTokenSource();

        var save = scheduler.SaveAsync(
            new RocketSessionDocument("main.rocket", "raw", 2, "main"),
            (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<RocketSessionDocument>(token);
            },
            (_, _) => { events.Add("persist"); return Task.FromResult<RocketSessionDocument?>(null); },
            (_, _) => { events.Add("didSave"); return Task.CompletedTask; },
            cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => save);
        Assert.AreEqual(0, events.Count);
    }
}
