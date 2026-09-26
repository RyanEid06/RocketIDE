using RocketIDE.App.Integration;
using RocketIDE.Core.Recovery;

namespace RocketIDE.App.Tests.Integration;

[TestClass]
public sealed class ShutdownRecoveryCoordinatorTests
{
    [TestMethod]
    public async Task FailedSaveRetainsLatestCheckpointAndUndecidedRecovery()
    {
        var store = new MemoryRecoveryStore { Current = new([Snapshot("pending", "older")], DateTimeOffset.UtcNow) };
        var coordinator = new ShutdownRecoveryCoordinator(store);
        await coordinator.BeginShutdownAsync([Snapshot("active", "latest text")], true, CancellationToken.None);
        await coordinator.CompleteShutdownAsync(savesSucceeded: false, preserveExisting: true, CancellationToken.None);

        Assert.AreEqual(0, store.ClearCount);
        Assert.AreEqual(2, store.Current.Snapshots.Count);
        Assert.AreEqual("latest text", store.Current.Snapshots.Single(s => s.OriginalPath == "active").Text);
    }

    [TestMethod]
    public async Task SuccessfulSaveClearsCheckpointOnlyAtCompletion()
    {
        var store = new MemoryRecoveryStore();
        var coordinator = new ShutdownRecoveryCoordinator(store);
        await coordinator.BeginShutdownAsync([Snapshot("active", "latest text")], false, CancellationToken.None);
        Assert.AreEqual(1, store.Current.Snapshots.Count);
        await coordinator.CompleteShutdownAsync(true, false, CancellationToken.None);
        Assert.AreEqual(1, store.ClearCount);
        Assert.AreEqual(0, store.Current.Snapshots.Count);
    }

    [TestMethod]
    public async Task InFlightPeriodicWriteCannotOverwriteShutdownCheckpoint()
    {
        var store = new MemoryRecoveryStore { PauseNextSave = true };
        var coordinator = new ShutdownRecoveryCoordinator(store);
        var periodic = coordinator.CheckpointAsync([Snapshot("active", "old")], CancellationToken.None);
        await store.SaveStarted.Task;
        var shutdown = coordinator.BeginShutdownAsync([Snapshot("active", "latest")], false, CancellationToken.None);
        store.ResumeSave.SetResult();
        await periodic;
        await shutdown;
        await coordinator.CheckpointAsync([], CancellationToken.None);
        await coordinator.CompleteShutdownAsync(false, false, CancellationToken.None);
        Assert.AreEqual("latest", store.Current.Snapshots.Single().Text);
        Assert.AreEqual(0, store.ClearCount);
    }

    private static RecoverySnapshot Snapshot(string path, string text) =>
        new(path, null, DateTimeOffset.UnixEpoch, 2, text, DateTimeOffset.UtcNow);

    private sealed class MemoryRecoveryStore : IRecoveryStore
    {
        public RecoverySet Current { get; set; } = new([], DateTimeOffset.UtcNow);
        public int ClearCount { get; private set; }
        public bool PauseNextSave { get; set; }
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RecoverySet> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        public async Task SaveAsync(RecoverySet set, CancellationToken cancellationToken)
        {
            if (PauseNextSave)
            {
                PauseNextSave = false;
                SaveStarted.SetResult();
                await ResumeSave.Task.WaitAsync(cancellationToken);
            }
            Current = set;
        }
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            Current = new([], DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }
    }
}
