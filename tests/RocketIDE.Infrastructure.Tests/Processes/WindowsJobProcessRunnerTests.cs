using System.Collections.Concurrent;
using RocketIDE.Core.Commands;
using RocketIDE.Core.Output;
using RocketIDE.Infrastructure.Processes;

namespace RocketIDE.Infrastructure.Tests.Processes;

[TestClass]
public sealed class WindowsJobProcessRunnerTests
{
    [TestMethod]
    public async Task RunAsync_CapturesStdoutAndStderr()
    {
        using var runner = new WindowsJobProcessRunner();
        var outputs = new ConcurrentQueue<ProcessOutput>();
        var progress = new InlineProgress<ProcessOutput>(outputs.Enqueue);
        var command = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
        var request = new ProcessStartRequest(
            command,
            ["/d", "/s", "/c", "echo stdout-line&echo stderr-line 1>&2"],
            Path.GetTempPath());

        var result = await runner.RunAsync(request, progress, CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsFalse(result.Cancelled);
        Assert.IsTrue(outputs.Any(item => item.Stream == ProcessOutputStream.StandardOutput && item.Text == "stdout-line"));
        // cmd.exe includes the separator before 1>&2 in the emitted stderr line.
        Assert.IsTrue(outputs.Any(item => item.Stream == ProcessOutputStream.StandardError && item.Text.TrimEnd() == "stderr-line"));
    }

    [TestMethod]
    public async Task StopActive_TerminatesRunningJobAndMarksResultCancelled()
    {
        using var runner = new WindowsJobProcessRunner();
        var command = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
        var request = new ProcessStartRequest(
            command,
            ["/d", "/s", "/c", "ping 127.0.0.1 -n 30 >nul"],
            Path.GetTempPath());

        var runTask = runner.RunAsync(request, new InlineProgress<ProcessOutput>(_ => { }), CancellationToken.None);
        await Task.Delay(250);
        runner.StopActive();
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(result.Cancelled);
    }

    [TestMethod]
    public async Task CancellationToken_TerminatesRunningJobAndMarksResultCancelled()
    {
        using var runner = new WindowsJobProcessRunner();
        using var cancellation = new CancellationTokenSource();
        var command = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
        var request = new ProcessStartRequest(
            command,
            ["/d", "/s", "/c", "ping 127.0.0.1 -n 30 >nul"],
            Path.GetTempPath());

        var runTask = runner.RunAsync(request, new InlineProgress<ProcessOutput>(_ => { }), cancellation.Token);
        await Task.Delay(250);
        cancellation.Cancel();
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(result.Cancelled);
    }

    [TestMethod]
    public async Task Dispose_TerminatesRunningJobAndMarksResultCancelled()
    {
        var runner = new WindowsJobProcessRunner();
        var command = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
        var request = new ProcessStartRequest(
            command,
            ["/d", "/s", "/c", "ping 127.0.0.1 -n 30 >nul"],
            Path.GetTempPath());

        var runTask = runner.RunAsync(request, new InlineProgress<ProcessOutput>(_ => { }), CancellationToken.None);
        await Task.Delay(250);
        runner.Dispose();
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(result.Cancelled);
    }

    [TestMethod]
    public async Task RunAsync_RejectsConcurrentProcess()
    {
        using var runner = new WindowsJobProcessRunner();
        var command = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
        var first = runner.RunAsync(
            new ProcessStartRequest(command, ["/d", "/s", "/c", "ping 127.0.0.1 -n 30 >nul"], Path.GetTempPath()),
            new InlineProgress<ProcessOutput>(_ => { }),
            CancellationToken.None);
        await Task.Delay(150);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => runner.RunAsync(
            new ProcessStartRequest(command, ["/d", "/s", "/c", "echo second"], Path.GetTempPath()),
            new InlineProgress<ProcessOutput>(_ => { }),
            CancellationToken.None));

        runner.StopActive();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
