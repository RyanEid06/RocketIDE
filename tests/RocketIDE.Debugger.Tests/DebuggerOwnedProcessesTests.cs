using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class DebuggerOwnedProcessesTests
{
    [TestMethod]
    public async Task KillEngineHosts_TerminatesOnlyOwnedWindowsChild()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rocketide-engine-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "EngHost.exe");
        File.Copy(Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe", executable);
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                Arguments = "/d /s /c ping 127.0.0.1 -n 30 >nul",
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            try
            {
                Assert.IsFalse(process.HasExited);
                DebuggerOwnedProcesses.KillEngineHosts(Environment.ProcessId);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                Assert.IsTrue(process.HasExited);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FindOwnedEngineHosts_SelectsOnlyEngineHostsDescendedFromThisIde()
    {
        var processes = new[]
        {
            new DebuggerOwnedProcesses.ProcessEntry(10, 1, "RocketIDE.exe"),
            new DebuggerOwnedProcesses.ProcessEntry(11, 10, "helper.exe"),
            new DebuggerOwnedProcesses.ProcessEntry(12, 11, "EngHost.exe"),
            new DebuggerOwnedProcesses.ProcessEntry(13, 10, "EngHost.exe"),
            new DebuggerOwnedProcesses.ProcessEntry(14, 2, "EngHost.exe"),
            new DebuggerOwnedProcesses.ProcessEntry(15, 14, "EngHost.exe"),
        };

        CollectionAssert.AreEquivalent(new[] { 12, 13 },
            DebuggerOwnedProcesses.FindOwnedEngineHosts(10, processes).ToArray());
    }
}
