using System.Diagnostics;
using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class DebuggerSynchronizationContextTests
{
    [TestMethod]
    public void Dispose_DetachesFromBlockedWorkerWithoutDisposingItsQueue()
    {
        var context = new DbgXCommandTransport.DebuggerSynchronizationContext(TimeSpan.FromMilliseconds(40));
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        context.Post(_ => { started.Set(); release.Wait(); }, null);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(1)));
        var elapsed = Stopwatch.StartNew();

        context.Dispose();

        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromMilliseconds(500));
        release.Set();
    }

    [TestMethod]
    public void Dispose_DropsLateDbgXContinuationWithoutThrowing()
    {
        var context = new DbgXCommandTransport.DebuggerSynchronizationContext();
        context.Dispose();
        var called = false;

        context.Post(_ => called = true, null);

        Assert.IsFalse(called);
    }
}
