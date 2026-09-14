using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class RocketNativeDebuggerTests
{
    [TestMethod]
    public async Task LaunchConfiguresSourcesBindsBreakpointsAndStopsAtRocketLocation()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateFile("src/main.rocket", "fn main():\n    return 0\n");
        var exe = temp.CreateFile("out/app.exe", "exe");
        var pdb = temp.CreateFile("out/app.pdb", "pdb");
        var map = temp.CreateFile("out/app.rocket.map.json", "{\"format\":\"rocket-source-map-1\",\"functions\":[{\"source\":\"src/main.rocket\"}]}");
        var transport = new FakeTransport
        {
            Responses =
            {
                ["|"] = ".  0    id: 1234 examine name: app.exe",
                ["g"] = "breakpoint hit",
                ["~"] = ".  0  Id: 1234.2abc Suspend: 1 Teb: 0 Unfrozen",
                ["kn"] = "00 00000000 00000000 app!main+0x4 [rocket:\\source\\main.rocket @ 2]",
                ["dv /t"] = "int answer = 0n42",
                ["ln @rip"] = "app!main+0x4 [rocket:\\source\\main.rocket @ 2]",
            },
        };
        await using var debugger = new RocketNativeDebugger(transport);
        RocketDebugStopLocation? stopped = null;
        debugger.Stopped += (_, e) => stopped = e.Location;

        await debugger.LaunchAsync(new RocketDebugLaunchRequest(
            exe, pdb, map, temp.Path, temp.Path, [], [new RocketDebugBreakpoint(source, 2)]), CancellationToken.None);

        Assert.AreEqual(RocketDebugSessionState.Stopped, debugger.State);
        Assert.IsNotNull(stopped);
        Assert.AreEqual(Path.GetFullPath(source), stopped.SourcePath);
        CollectionAssert.Contains(transport.Commands, ".expr /s masm");
        CollectionAssert.Contains(transport.Commands, ".lines -e");
        CollectionAssert.Contains(transport.Commands, "bp `main.rocket:2`");
        CollectionAssert.Contains(transport.Commands, "g");
        Assert.AreEqual(1, debugger.Threads.Count);
        Assert.AreEqual(1, debugger.Locals.Count);
        Assert.IsTrue(debugger.Breakpoints.Single().IsBound);
    }

    [TestMethod]
    public async Task PauseUsesDebugBreakProcessBoundaryWhileRunning()
    {
        var transport = new FakeTransport();
        await using var debugger = new RocketNativeDebugger(transport);
        debugger.SetTestState(RocketDebugSessionState.Running, processId: 321);

        await debugger.PauseAsync(CancellationToken.None);

        Assert.AreEqual(321, transport.BrokenProcessId);
    }

    [TestMethod]
    public async Task SetBreakpointsRejectsChangesWhileTargetIsRunning()
    {
        var transport = new FakeTransport();
        await using var debugger = new RocketNativeDebugger(transport);
        debugger.SetTestState(RocketDebugSessionState.Running, processId: 321);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            debugger.SetBreakpointsAsync([], CancellationToken.None));
    }

    [TestMethod]
    public async Task ExplicitStopSurvivesInterruptedRunningEngineRequest()
    {
        var pendingRun = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeTransport
        {
            PendingRunCommand = pendingRun,
            FaultPendingRunOnBreak = true,
        };
        await using var debugger = new RocketNativeDebugger(transport);
        debugger.SetTestState(RocketDebugSessionState.Stopped, processId: 321);

        var continueTask = debugger.ContinueAsync(CancellationToken.None);
        Assert.AreEqual(RocketDebugSessionState.Running, debugger.State);

        await debugger.StopAsync(CancellationToken.None);
        await continueTask;

        Assert.AreEqual(321, transport.BrokenProcessId);
        Assert.IsTrue(transport.Stopped);
        Assert.AreEqual(RocketDebugSessionState.Terminated, debugger.State);
    }

    private sealed class FakeTransport : IDebuggerCommandTransport
    {
        public event EventHandler<RocketDebugOutputEventArgs>? OutputReceived;
        public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);
        public List<string> Commands { get; } = [];
        public int? BrokenProcessId { get; private set; }
        public bool Stopped { get; private set; }
        public TaskCompletionSource<string>? PendingRunCommand { get; init; }
        public bool FaultPendingRunOnBreak { get; init; }

        public Task CreateProcessAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            OutputReceived?.Invoke(this, new RocketDebugOutputEventArgs("created"));
            return Task.CompletedTask;
        }
        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (command == "g" && PendingRunCommand is not null) return PendingRunCommand.Task;
            return Task.FromResult(Responses.TryGetValue(command, out var result) ? result : string.Empty);
        }
        public Task BreakAsync(int processId, CancellationToken cancellationToken)
        {
            BrokenProcessId = processId;
            if (FaultPendingRunOnBreak)
            {
                PendingRunCommand?.TrySetException(new InvalidOperationException("DbgEng execution was interrupted by Break."));
            }
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-debug-session-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public string CreateFile(string relative, string text)
        {
            var path = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
