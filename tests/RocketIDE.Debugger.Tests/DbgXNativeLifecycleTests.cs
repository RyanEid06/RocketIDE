using System.Diagnostics;
using System.Runtime.InteropServices;
using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class DbgXNativeLifecycleTests
{
    [TestMethod]
    public async Task StalledEngineCleanupDoesNotLeaveAnAutomaticallyRestartedHost()
    {
        IReadOnlyList<int> OwnedHosts() => DebuggerOwnedProcesses.FindOwnedEngineHosts(
            Environment.ProcessId, DebuggerOwnedProcesses.SnapshotProcesses());
        var before = OwnedHosts().ToHashSet();
        await using var transport = await DbgXCommandTransport.CreateAsync();
        try
        {
            var executable = Environment.GetEnvironmentVariable("ComSpec")!;
            using var launch = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await transport.CreateProcessAsync(executable, ["/d", "/c", "exit", "0"], Path.GetDirectoryName(executable)!, launch.Token);
            using var host = Process.GetProcessById(OwnedHosts().Single(id => !before.Contains(id)));
            Assert.AreEqual(0, NtSuspendProcess(host.Handle));
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await transport.ExecuteAsync("r", deadline.Token));
            transport.ForceTerminateOwnedProcesses();
            await transport.DisposeAsync();
            await Task.Delay(700);
            var remaining = OwnedHosts().Where(id => !before.Contains(id)).ToArray();
            Assert.AreEqual(0, remaining.Length, "DbgX must not leave its automatic replacement engine after forced cleanup.");
        }
        finally { DebuggerOwnedProcesses.KillEngineHosts(Environment.ProcessId); }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);
}
