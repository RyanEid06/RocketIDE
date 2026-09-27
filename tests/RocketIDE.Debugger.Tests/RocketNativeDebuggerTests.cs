using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class RocketNativeDebuggerTests
{
    [TestMethod]
    public async Task UserCancelledLaunchTerminatesAndReleasesItsTransport()
    {
        using var fixture = new DebugFixture();
        fixture.Transport.PendingCreate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var debugger = new RocketNativeDebugger(fixture.Transport);
        using var cancel = new CancellationTokenSource();
        var launch = debugger.LaunchAsync(fixture.Request, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => launch);
        Assert.AreEqual(RocketDebugSessionState.Terminated, debugger.State);
        Assert.IsTrue(fixture.Transport.Disposed);
    }

    [TestMethod]
    public async Task TopRocketFrameProvidesLocationWhenLnOmitsSource()
    {
        using var fixture = new DebugFixture();
        fixture.Transport.Responses["ln @rip"] = "app!main+0x3f";
        await using var debugger = new RocketNativeDebugger(fixture.Transport);
        await debugger.LaunchAsync(fixture.Request, CancellationToken.None);
        Assert.AreEqual(2, debugger.CurrentLocation?.Line);
    }

    [TestMethod]
    public async Task DisabledBreakpointFromAnotherTargetDoesNotBlockBinding()
    {
        using var fixture = new DebugFixture();
        await using var debugger = new RocketNativeDebugger(fixture.Transport);
        await debugger.LaunchAsync(fixture.Request, CancellationToken.None);
        var disabled = new RocketDebugBreakpoint(Path.GetFullPath("other-target.rocket"), 1) { IsEnabled = false };
        await debugger.SetBreakpointsAsync([disabled], CancellationToken.None);
        Assert.AreEqual(disabled.SourcePath, debugger.Breakpoints.Single().SourcePath);
        Assert.IsFalse(debugger.Breakpoints.Single().IsBound);
    }

    [TestMethod]
    public async Task RunToCursorSetupFaultCannotLeaveTemporaryBreakpoint()
    {
        using var fixture = new DebugFixture();
        await using var debugger = new RocketNativeDebugger(fixture.Transport);
        await debugger.LaunchAsync(fixture.Request, CancellationToken.None);
        fixture.Transport.ThrowCommand = "bp /1 `main.rocket:3`";
        await Assert.ThrowsAsync<IOException>(() => debugger.RunToCursorAsync(fixture.Source, 3, CancellationToken.None));
        Assert.IsFalse(debugger.HasTemporaryBreakpoint);
        Assert.AreEqual(RocketDebugSessionState.Faulted, debugger.State);
        Assert.IsTrue(fixture.Transport.Disposed);
    }

    [TestMethod]
    public async Task PostStopProcessInspectionHasABoundedDeadline()
    {
        using var fixture = new DebugFixture();
        await using var debugger = new RocketNativeDebugger(fixture.Transport, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        await debugger.LaunchAsync(fixture.Request, CancellationToken.None);
        fixture.Transport.HungCommand = "|";
        await debugger.StepOverAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(RocketDebugSessionState.Faulted, debugger.State);
        Assert.IsTrue(fixture.Transport.Disposed);
    }

    [TestMethod]
    public async Task NativeTopFrameDoesNotBorrowTheRocketCallersLocation()
    {
        using var fixture = new DebugFixture();
        fixture.Transport.Responses["kn"] = "00 00000000 00000000 ntdll!Sleep\n01 00000000 00000000 app!main [rocket:\\source\\main.rocket @ 2]";
        fixture.Transport.Responses["ln @rip"] = "ntdll!Sleep";
        await using var debugger = new RocketNativeDebugger(fixture.Transport);
        await debugger.LaunchAsync(fixture.Request, CancellationToken.None);
        Assert.IsNull(debugger.Frames[0].SourcePath);
        Assert.IsNull(debugger.CurrentLocation);
        await debugger.SelectFrameAsync(1, CancellationToken.None);
        Assert.AreEqual(2, debugger.CurrentLocation?.Line);
        await debugger.SelectFrameAsync(0, CancellationToken.None);
        Assert.IsNull(debugger.CurrentLocation);
    }

    [TestMethod]
    public async Task DisabledBreakpointsAreRetainedWithoutBeingBound()
    {
        using var fixture = new DebugFixture();
        await using var debugger = new RocketNativeDebugger(fixture.Transport);
        await debugger.LaunchAsync(fixture.Request, CancellationToken.None);
        fixture.Transport.Commands.Clear();
        await debugger.SetBreakpointsAsync([new RocketDebugBreakpoint(fixture.Source, 2) { IsEnabled = false }], CancellationToken.None);
        Assert.IsFalse(debugger.Breakpoints.Single().IsBound);
        Assert.IsFalse(debugger.Breakpoints.Single().IsEnabled);
        Assert.IsFalse(fixture.Transport.Commands.Any(command => command.StartsWith("bp ")));
    }

    [TestMethod]
    public async Task EvaluationUsesNativeIdentifiersAndRejectsUnsupportedOrRunningRequests()
    {
        var transport = new FakeTransport();
        transport.Responses["?? value"] = "int64 0n21";
        transport.Responses["?? missing"] = "Couldn't resolve error at 'missing'";
        await using var debugger = new RocketNativeDebugger(transport);
        debugger.SetTestState(RocketDebugSessionState.Stopped, null);
        var result = await debugger.EvaluateAsync("value", CancellationToken.None);
        Assert.IsTrue(result.IsAvailable);
        Assert.AreEqual("int64 0n21", result.Value);
        Assert.IsFalse((await debugger.EvaluateAsync("missing", CancellationToken.None)).IsAvailable);
        foreach (var expression in new[] { "", "value;g", "pair.left", "f()", "@rip", new string('a', 129) })
            Assert.IsFalse((await debugger.EvaluateAsync(expression, CancellationToken.None)).IsAvailable);
        CollectionAssert.AreEqual(new[] { "?? value", "?? missing" }, transport.Commands);
        debugger.SetTestState(RocketDebugSessionState.Running, null);
        Assert.IsFalse((await debugger.EvaluateAsync("value", CancellationToken.None)).IsAvailable);
        Assert.AreEqual(2, transport.Commands.Count);
    }

    [TestMethod]
    public async Task SlowEvaluationIsBoundedFaultsSessionAndRejectsLateResults()
    {
        var transport = new FakeTransport { HungCommand = "?? value" };
        await using var debugger = new RocketNativeDebugger(transport, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        debugger.SetTestState(RocketDebugSessionState.Stopped, null);
        var result = await debugger.EvaluateAsync("value", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(result.IsAvailable);
        Assert.AreEqual(RocketDebugSessionState.Faulted, debugger.State);
        Assert.IsTrue(transport.Disposed);
        Assert.IsFalse((await debugger.EvaluateAsync("value", CancellationToken.None)).IsAvailable);
        Assert.AreEqual(1, transport.Commands.Count);
    }

    [TestMethod]
    public async Task RunToCursorClearsItsTemporaryBreakpointAfterAnyStop()
    {
        using var fixture = new DebugFixture();
        await using var debugger = new RocketNativeDebugger(fixture.Transport);
        await debugger.LaunchAsync(fixture.Request, CancellationToken.None);
        fixture.Transport.Commands.Clear();
        await debugger.RunToCursorAsync(fixture.Source, 3, CancellationToken.None);
        var commands = fixture.Transport.Commands;
        var temporary = commands.IndexOf("bp /1 `main.rocket:3`");
        var resume = commands.IndexOf("g");
        Assert.IsTrue(temporary >= 0 && resume > temporary);
        Assert.IsTrue(commands.IndexOf("bc *") > resume);
        Assert.AreEqual(1, debugger.Breakpoints.Count);
        Assert.AreEqual(2, debugger.Breakpoints.Single().Line);
        Assert.IsFalse(debugger.HasTemporaryBreakpoint);
    }

    [TestMethod]
    public async Task RunToCursorCancellationStopsAndClearsTemporaryBreakpoint()
    {
        using var fixture = new DebugFixture();
        await using var debugger = new RocketNativeDebugger(fixture.Transport);
        await debugger.LaunchAsync(fixture.Request, CancellationToken.None);
        fixture.Transport.PendingRunCommand = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.FaultPendingRunOnBreak = true;
        using var cancel = new CancellationTokenSource();
        var run = debugger.RunToCursorAsync(fixture.Source, 3, cancel.Token);
        Assert.IsTrue(debugger.HasTemporaryBreakpoint);
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.IsFalse(debugger.HasTemporaryBreakpoint);
        Assert.AreEqual(RocketDebugSessionState.Terminated, debugger.State);
        Assert.IsTrue(fixture.Transport.Disposed);
    }

    [TestMethod]
    public async Task StopPublishesTerminatingAndDisposesTheOwnedEngine()
    {
        var transport = new FakeTransport();
        await using var debugger = new RocketNativeDebugger(transport);
        debugger.SetTestState(RocketDebugSessionState.Stopped, null);
        var states = new List<RocketDebugSessionState>();
        debugger.StateChanged += (_, e) => states.Add(e.State);
        await debugger.StopAsync(CancellationToken.None);
        CollectionAssert.AreEqual(new[] { RocketDebugSessionState.Terminating, RocketDebugSessionState.Terminated }, states);
        Assert.IsTrue(transport.Disposed);
    }

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
        Assert.IsTrue(transport.Commands.IndexOf("ld \"app\"") >= 0 &&
            transport.Commands.IndexOf("ld \"app\"") < transport.Commands.IndexOf("bp `main.rocket:2`"),
            "Load the target PDB before resolving source-line breakpoints.");
        Assert.AreEqual(1, debugger.Threads.Count);
        Assert.AreEqual(1, debugger.Locals.Count);
        Assert.IsTrue(debugger.Breakpoints.Single().IsBound);

        await debugger.SelectFrameAsync(1, CancellationToken.None);
        transport.Commands.Clear();
        await debugger.SelectThreadAsync(0, CancellationToken.None);
        Assert.IsTrue(transport.Commands.IndexOf(".frame 0") > transport.Commands.IndexOf("~0s") &&
            transport.Commands.IndexOf(".frame 0") < transport.Commands.IndexOf("dv /t"),
            "Thread selection must reset the local-variable scope to its top frame.");
    }

    [TestMethod]
    public async Task LaunchDoesNotReportDeferredSourceBreakpointAsBound()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateFile("main.rocket", "fn main() -> Int:\n    return 0\n");
        var exe = temp.CreateFile("app.exe", "exe");
        var pdb = temp.CreateFile("app.pdb", "pdb");
        var map = temp.CreateFile("app.rocket.map.json", "{\"format\":\"rocket-source-map-1\",\"functions\":[{\"source\":\"main.rocket\"}]}");
        var transport = new FakeTransport
        {
            Responses =
            {
                ["|"] = ".  0 id: 1234 create name: app.exe",
                ["bp `main.rocket:2`"] = "Bp expression '`main.rocket:2`' could not be resolved, adding deferred bp",
            },
        };
        await using var debugger = new RocketNativeDebugger(transport);

        await debugger.LaunchAsync(new RocketDebugLaunchRequest(exe, pdb, map,
            temp.Path, temp.Path, [], [new RocketDebugBreakpoint(source, 2)]), CancellationToken.None);

        Assert.IsFalse(debugger.Breakpoints.Single().IsBound);
        StringAssert.Contains(debugger.Breakpoints.Single().Message!, "deferred");
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

    [TestMethod]
    public async Task StopAsync_HungTransportHonorsCallerShutdownToken()
    {
        var transport = new FakeTransport { HungStop = true };
        await using var debugger = new RocketNativeDebugger(transport, TimeSpan.FromMilliseconds(50));
        debugger.SetTestState(RocketDebugSessionState.Stopped, processId: null);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            debugger.StopAsync(cancellation.Token).WaitAsync(TimeSpan.FromMilliseconds(500)));
    }

    [TestMethod]
    public async Task DisposeAsync_DetachesFromHungDebuggerTransportWithinBudget()
    {
        var transport = new FakeTransport { HungStop = true, HungDispose = true };
        var debugger = new RocketNativeDebugger(transport, TimeSpan.FromMilliseconds(50));
        debugger.SetTestState(RocketDebugSessionState.Stopped, processId: null);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        await debugger.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(500));

        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public async Task ForceTerminateOwnedProcesses_KillsKnownDebuggeeTree()
    {
        var command = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = command,
            Arguments = "/d /s /c ping 127.0.0.1 -n 30 >nul",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        try
        {
            await using var debugger = new RocketNativeDebugger(new FakeTransport());
            debugger.SetTestState(RocketDebugSessionState.Stopped, process.Id);

            debugger.ForceTerminateOwnedProcesses();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));

            Assert.IsTrue(process.HasExited);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private sealed class FakeTransport : IDebuggerCommandTransport
    {
        public event EventHandler<RocketDebugOutputEventArgs>? OutputReceived;
        public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);
        public List<string> Commands { get; } = [];
        public int? BrokenProcessId { get; private set; }
        public bool Stopped { get; private set; }
        public TaskCompletionSource<string>? PendingRunCommand { get; set; }
        public bool FaultPendingRunOnBreak { get; set; }
        public string? HungCommand { get; set; }
        public string? ThrowCommand { get; set; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource? PendingCreate { get; set; }
        public bool HungStop { get; init; }
        public bool HungDispose { get; init; }

        public Task CreateProcessAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            OutputReceived?.Invoke(this, new RocketDebugOutputEventArgs("created"));
            return PendingCreate?.Task ?? Task.CompletedTask;
        }
        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (command == ThrowCommand) throw new IOException("Native command disconnected after acceptance.");
            if (command == HungCommand) return new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
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
            if (HungStop) return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
            Stopped = true;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return HungDispose ? new ValueTask(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task) : ValueTask.CompletedTask;
        }
    }

    private sealed class DebugFixture : IDisposable
    {
        private readonly TempDirectory _temp = new();
        public DebugFixture()
        {
            Source = _temp.CreateFile("main.rocket", "fn main() -> Int:\n    let value = 21\n    return value\n");
            Request = new RocketDebugLaunchRequest(_temp.CreateFile("app.exe", "exe"), _temp.CreateFile("app.pdb", "pdb"),
                _temp.CreateFile("app.rocket.map.json", "{\"format\":\"rocket-source-map-1\",\"functions\":[{\"source\":\"main.rocket\"}]}"),
                _temp.Path, _temp.Path, [], [new RocketDebugBreakpoint(Source, 2)]);
            Transport.Responses["|"] = ".  0 id: 1234 create name: app.exe";
            Transport.Responses["kn"] = "00 00000000 00000000 app!main [rocket:\\source\\main.rocket @ 2]";
            Transport.Responses["ln @rip"] = "app!main [rocket:\\source\\main.rocket @ 2]";
        }
        public string Source { get; }
        public RocketDebugLaunchRequest Request { get; }
        public FakeTransport Transport { get; } = new();
        public void Dispose() => _temp.Dispose();
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
