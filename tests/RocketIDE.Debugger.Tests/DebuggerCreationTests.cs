using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class DebuggerCreationTests
{
    [TestMethod]
    public async Task CancelledCreationDisposesALateOwnedResultBeforeReleasingItsContext()
    {
        var creation = new TaskCompletionSource<OwnedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        var request = DebuggerCreation.AwaitAsync(creation.Task, TimeSpan.FromSeconds(1), () => cleanup.TrySetResult(), cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => request);
        Assert.IsFalse(cleanup.Task.IsCompleted, "Keep the context alive until the late creation is owned and disposed.");
        var owned = new OwnedResource();
        creation.SetResult(owned);
        await cleanup.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsTrue(owned.Disposed);
    }

    private sealed class OwnedResource : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
