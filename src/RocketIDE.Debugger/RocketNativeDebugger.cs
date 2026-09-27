namespace RocketIDE.Debugger;

public sealed class RocketNativeDebugger : IRocketNativeDebugger
{
    private readonly IDebuggerCommandTransport _transport;
    private readonly TimeSpan _disposalTimeout;
    private readonly TimeSpan _operationTimeout;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _inspectionGate = new(1, 1);
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private readonly CancellationTokenSource _sessionCancellation = new();
    private RocketDebugSessionState _state = RocketDebugSessionState.Idle;
    private RocketDebugSourceMap? _sourceMap;
    private IReadOnlyList<RocketDebugBreakpoint> _breakpoints = [];
    private IReadOnlyList<RocketDebugThread> _threads = [];
    private IReadOnlyList<RocketDebugStackFrame> _frames = [];
    private IReadOnlyList<RocketDebugVariable> _locals = [];
    private RocketDebugStopLocation? _currentLocation;
    private int? _processId;
    private Task? _activeExecution;
    private volatile bool _stopRequested;
    private volatile bool _hasTemporaryBreakpoint;
    private int _transportDisposed;
    private Task? _transportDisposal;
    private bool _disposed;

    public RocketNativeDebugger(IDebuggerCommandTransport transport, TimeSpan? disposalTimeout = null, TimeSpan? operationTimeout = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _disposalTimeout = disposalTimeout ?? TimeSpan.FromSeconds(2);
        _operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(2);
        if (_disposalTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(disposalTimeout));
        if (_operationTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        _transport.OutputReceived += Transport_OutputReceived;
    }

    public event EventHandler<RocketDebugStateChangedEventArgs>? StateChanged;
    public event EventHandler<RocketDebugOutputEventArgs>? OutputReceived;
    public event EventHandler<RocketDebugStoppedEventArgs>? Stopped;
    public RocketDebugSessionState State { get { lock (_stateLock) return _state; } }
    public IReadOnlyList<RocketDebugBreakpoint> Breakpoints { get { lock (_stateLock) return _breakpoints; } }
    public IReadOnlyList<RocketDebugThread> Threads { get { lock (_stateLock) return _threads; } }
    public IReadOnlyList<RocketDebugStackFrame> Frames { get { lock (_stateLock) return _frames; } }
    public IReadOnlyList<RocketDebugVariable> Locals { get { lock (_stateLock) return _locals; } }
    public RocketDebugStopLocation? CurrentLocation { get { lock (_stateLock) return _currentLocation; } }
    public bool HasTemporaryBreakpoint => _hasTemporaryBreakpoint;

    public async Task LaunchAsync(RocketDebugLaunchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (State != RocketDebugSessionState.Idle)
            throw new InvalidOperationException("Create a new debugger session before launching another target.");
        ValidateLaunchArtifacts(request);
        var sourceMap = RocketDebugSourceMap.Read(request.SourceMapPath, request.SourceRoot);
        ValidateBreakpointSources(request.Breakpoints, sourceMap);
        lock (_stateLock) { _sourceMap = sourceMap; _breakpoints = request.Breakpoints.ToArray(); }
        SetState(RocketDebugSessionState.Launching, "Launching Rocket debug target…");
        try
        {
            // Engine initialization gets its own bound; stopped inspection has a shorter budget.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCancellation.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var token = deadline.Token;
            await _transport.CreateProcessAsync(request.ExecutablePath, request.Arguments, request.WorkingDirectory, token).WaitAsync(token).ConfigureAwait(false);
            await ConfigureEngineAsync(sourceMap, token).ConfigureAwait(false);
            await ExecuteEngineAsync(DbgEngProtocol.BuildLoadSymbolsCommand(request.ExecutablePath), token).ConfigureAwait(false);
            var processText = await ExecuteEngineAsync("|", token).ConfigureAwait(false);
            lock (_stateLock) _processId = DbgEngProtocol.ParseCurrentProcessId(processText)
                ?? throw new InvalidOperationException("DbgEng did not report a live debuggee after launch.");
            await BindBreakpointsAsync(request.Breakpoints, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (_stateLock) _state = RocketDebugSessionState.Stopped;
            await ContinueAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            if (!_stopRequested) await FaultAsync(exception.Message).ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetBreakpointsAsync(IReadOnlyList<RocketDebugBreakpoint> breakpoints, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        ThrowIfDisposed();
        if (State is RocketDebugSessionState.Idle or RocketDebugSessionState.Terminated or RocketDebugSessionState.Faulted)
        {
            lock (_stateLock) _breakpoints = breakpoints.Select(item => item with { IsBound = false, Message = null }).ToArray();
            return;
        }
        await InspectAsync(async token =>
        {
            ValidateBreakpointSources(breakpoints, _sourceMap!);
            await BindBreakpointsAsync(breakpoints, token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task ContinueAsync(CancellationToken cancellationToken) => ExecuteRunCommandAsync("g", "continue", cancellationToken);
    public Task StepOverAsync(CancellationToken cancellationToken) => ExecuteRunCommandAsync("p", "step over", cancellationToken);
    public Task StepIntoAsync(CancellationToken cancellationToken) => ExecuteRunCommandAsync("t", "step into", cancellationToken);
    public Task StepOutAsync(CancellationToken cancellationToken) => ExecuteRunCommandAsync("gu", "step out", cancellationToken);

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (State != RocketDebugSessionState.Running) return;
        int processId;
        lock (_stateLock) processId = _processId ?? throw new InvalidOperationException("The debugger process ID is unavailable.");
        await _transport.BreakAsync(processId, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SelectThreadAsync(int debuggerThreadIndex, CancellationToken cancellationToken)
    {
        await InspectAsync(async token =>
        {
            await ExecuteEngineAsync(DbgEngProtocol.BuildSelectThreadCommand(debuggerThreadIndex), token).ConfigureAwait(false);
            await ExecuteEngineAsync(DbgEngProtocol.BuildSelectFrameCommand(0), token).ConfigureAwait(false);
            await RefreshStoppedStateAsync("thread selection", token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        if (State == RocketDebugSessionState.Stopped) Stopped?.Invoke(this, new(CurrentLocation));
    }

    public async Task SelectFrameAsync(int frameIndex, CancellationToken cancellationToken)
    {
        await InspectAsync(async token =>
        {
            await ExecuteEngineAsync(DbgEngProtocol.BuildSelectFrameCommand(frameIndex), token).ConfigureAwait(false);
            var locals = await ExecuteEngineAsync("dv /t", token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                var selected = _frames.FirstOrDefault(frame => frame.Index == frameIndex);
                _currentLocation = selected?.SourcePath is not null && selected.Line is > 0
                    ? new(selected.SourcePath, selected.Line.Value, "frame selection") : null;
                _locals = DbgEngProtocol.ParseLocals(locals);
            }
            return true;
        }, cancellationToken).ConfigureAwait(false);
        if (State == RocketDebugSessionState.Stopped) Stopped?.Invoke(this, new(CurrentLocation));
    }

    public async Task<RocketDebugEvaluation> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        if (!DbgEngProtocol.IsSupportedExpression(expression))
            return new(expression, "Unsupported: use a local or parameter identifier (up to 128 characters). Members, operators and calls are unavailable.", false);
        if (_disposed || State != RocketDebugSessionState.Stopped)
            return new(expression, "Unavailable: pause the target and select a Rocket frame.", false);
        try
        {
            return await InspectAsync(async token =>
                DbgEngProtocol.ParseEvaluation(expression, await ExecuteEngineAsync("?? " + expression, token).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or InvalidOperationException)
        {
            return new(expression, "Unavailable: " + exception.Message, false);
        }
    }

    public async Task RunToCursorAsync(string sourcePath, int line, CancellationToken cancellationToken)
    {
        var temporary = new RocketDebugBreakpoint(sourcePath, line);
        try
        {
        await InspectAsync(async token =>
        {
            ValidateBreakpointSources([temporary], _sourceMap!);
            _hasTemporaryBreakpoint = true;
            var command = DbgEngProtocol.BuildSourceBreakpointCommand(Path.GetFileName(sourcePath), line).Replace("bp ", "bp /1 ", StringComparison.Ordinal);
            var error = FindBreakpointError(await ExecuteEngineAsync(command, token).ConfigureAwait(false));
            if (error is not null)
            {
                await BindBreakpointsAsync(Breakpoints, token).ConfigureAwait(false);
                throw new InvalidOperationException(error);
            }
            return true;
        }, cancellationToken).ConfigureAwait(false);
        await ContinueAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            if (_hasTemporaryBreakpoint) await FaultAsync(exception.Message).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_disposalTimeout);
        await _stopGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            if (_disposed || State is RocketDebugSessionState.Idle or RocketDebugSessionState.Terminated) return;
            var wasRunning = State == RocketDebugSessionState.Running;
            _stopRequested = true;
            _sessionCancellation.Cancel();
            SetState(RocketDebugSessionState.Terminating, "Debugger: stopping…");
            try
            {
                if (wasRunning && _processId is int pid)
                {
                    try { await _transport.BreakAsync(pid, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
                    catch (Exception) when (!deadline.IsCancellationRequested) { }
                    if (_activeExecution is Task active)
                        await active.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                if (Volatile.Read(ref _transportDisposed) == 0)
                {
                    await _transport.StopAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                    lock (_stateLock) _processId = null;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                OutputReceived?.Invoke(this, new("Debugger stop used bounded process cleanup: " + exception.Message));
            }
            finally
            {
                ForceTerminateOwnedProcesses();
                await DisposeTransportAsync(deadline.Token).ConfigureAwait(false);
                ClearSessionSnapshot();
                SetState(RocketDebugSessionState.Terminated, "Rocket debug session stopped.");
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally { _stopGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        using var deadline = new CancellationTokenSource(_disposalTimeout);
        try { await StopAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally
        {
            _disposed = true;
            _stopRequested = true;
            _sessionCancellation.Cancel();
            ForceTerminateOwnedProcesses();
            await DisposeTransportAsync(deadline.Token).ConfigureAwait(false);
            _transport.OutputReceived -= Transport_OutputReceived;
            GC.SuppressFinalize(this);
        }
    }

    private async Task DisposeTransportAsync(CancellationToken token)
    {
        Task disposal;
        lock (_stateLock)
        {
            _transportDisposed = 1;
            disposal = _transportDisposal ??= _transport.DisposeAsync().AsTask();
        }
        try { await disposal.WaitAsync(token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ForceTerminateOwnedProcesses(); }
    }

    private async Task FaultAsync(string message)
    {
        _stopRequested = true;
        _sessionCancellation.Cancel();
        ForceTerminateOwnedProcesses();
        using var deadline = new CancellationTokenSource(_disposalTimeout);
        await DisposeTransportAsync(deadline.Token).ConfigureAwait(false);
        ClearSessionSnapshot();
        SetState(RocketDebugSessionState.Faulted, "Debugger: " + message);
    }

    private async Task<T> InspectAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken, bool refreshing = false)
    {
        ThrowIfDisposed();
        if (!await _inspectionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A debugger inspection is already in progress.");
        try
        {
            if (!refreshing) EnsureStopped();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCancellation.Token);
            deadline.CancelAfter(_operationTimeout);
            try { return await action(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (!_stopRequested) await FaultAsync("Inspection cancelled or exceeded its deadline; restart debugging to continue.").ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested && !_sessionCancellation.IsCancellationRequested)
                    throw new TimeoutException("Debugger inspection timed out.");
                throw;
            }
        }
        finally { _inspectionGate.Release(); }
    }

    private Task<string> ExecuteEngineAsync(string command, CancellationToken token) =>
        _transport.ExecuteAsync(command, token).WaitAsync(token);

    private async Task ExecuteRunCommandAsync(string command, string reason, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!await _inspectionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Wait for debugger inspection before resuming.");
        Task execution;
        try
        {
            EnsureStopped();
            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateLock) { _threads = []; _frames = []; _locals = []; _currentLocation = null; }
            SetState(RocketDebugSessionState.Running, $"Rocket debugger: {reason}…");
            // Release before executing: fake or native completion can be synchronous.
        }
        finally { _inspectionGate.Release(); }
        execution = ExecuteAndRefreshAsync(command, reason);
        _activeExecution = execution;
        try { await execution.ConfigureAwait(false); }
        finally { if (ReferenceEquals(_activeExecution, execution)) _activeExecution = null; }
    }

    private async Task ExecuteAndRefreshAsync(string command, string reason)
    {
        try
        {
            // Running requests are ended by Pause/Stop, never abandoned into the engine queue.
            await _transport.ExecuteRunAsync(command, _sessionCancellation.Token).WaitAsync(_sessionCancellation.Token).ConfigureAwait(false);
            if (_stopRequested) return;
            var pid = await InspectAsync(async token =>
                DbgEngProtocol.ParseCurrentProcessId(await ExecuteEngineAsync("|", token).ConfigureAwait(false)),
                _sessionCancellation.Token, refreshing: true).ConfigureAwait(false);
            if (pid is null)
            {
                lock (_stateLock) _processId = null;
                using var deadline = new CancellationTokenSource(_disposalTimeout);
                await DisposeTransportAsync(deadline.Token).ConfigureAwait(false);
                ClearSessionSnapshot();
                SetState(RocketDebugSessionState.Terminated, "Rocket debug target exited.");
                return;
            }
            lock (_stateLock) _processId = pid;
            await InspectAsync(async token =>
            {
                if (_hasTemporaryBreakpoint) await BindBreakpointsAsync(Breakpoints, token).ConfigureAwait(false);
                await RefreshStoppedStateAsync(reason, token).ConfigureAwait(false);
                return true;
            }, _sessionCancellation.Token, refreshing: true).ConfigureAwait(false);
            if (_stopRequested) return;
            SetState(RocketDebugSessionState.Stopped, "Rocket debug target stopped.");
            Stopped?.Invoke(this, new(CurrentLocation));
        }
        catch (Exception exception)
        {
            if (_stopRequested) return;
            await FaultAsync(exception.Message).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RefreshStoppedStateAsync(string reason, CancellationToken token)
    {
        var sourceMap = _sourceMap ?? throw new InvalidOperationException("Rocket debug source map is unavailable.");
        var threads = await ExecuteEngineAsync("~", token).ConfigureAwait(false);
        var frames = await ExecuteEngineAsync("kn", token).ConfigureAwait(false);
        var locals = await ExecuteEngineAsync("dv /t", token).ConfigureAwait(false);
        var location = await ExecuteEngineAsync("ln @rip", token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            _threads = DbgEngProtocol.ParseThreads(threads);
            _frames = DbgEngProtocol.ParseStackFrames(frames, sourceMap);
            _locals = DbgEngProtocol.ParseLocals(locals);
            _currentLocation = DbgEngProtocol.ParseCurrentLocation(location, sourceMap, reason);
            if (_currentLocation is null && _frames.FirstOrDefault() is { SourcePath: not null, Line: > 0 } top)
                _currentLocation = new(top.SourcePath, top.Line.Value, reason);
        }
    }

    private async Task ConfigureEngineAsync(RocketDebugSourceMap sourceMap, CancellationToken token)
    {
        await ExecuteEngineAsync(".expr /s masm", token).ConfigureAwait(false);
        await ExecuteEngineAsync(".lines -e", token).ConfigureAwait(false);
        await ExecuteEngineAsync("l+t", token).ConfigureAwait(false);
        foreach (var directory in sourceMap.Sources.Select(Path.GetDirectoryName).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            await ExecuteEngineAsync(DbgEngProtocol.BuildSourcePathCommand(directory!), token).ConfigureAwait(false);
    }

    private async Task BindBreakpointsAsync(IReadOnlyList<RocketDebugBreakpoint> breakpoints, CancellationToken token)
    {
        await ExecuteEngineAsync("bc *", token).ConfigureAwait(false);
        _hasTemporaryBreakpoint = false;
        var bound = new List<RocketDebugBreakpoint>(breakpoints.Count);
        foreach (var breakpoint in breakpoints)
        {
            if (!breakpoint.IsEnabled) { bound.Add(breakpoint with { IsBound = false, Message = null }); continue; }
            var result = await ExecuteEngineAsync(DbgEngProtocol.BuildSourceBreakpointCommand(Path.GetFileName(breakpoint.SourcePath), breakpoint.Line), token).ConfigureAwait(false);
            var error = FindBreakpointError(result);
            bound.Add(breakpoint with { IsBound = error is null, Message = error });
        }
        token.ThrowIfCancellationRequested();
        lock (_stateLock) _breakpoints = bound;
    }

    private void ClearSessionSnapshot()
    {
        lock (_stateLock)
        {
            _processId = null; _threads = []; _frames = []; _locals = []; _currentLocation = null;
            _breakpoints = _breakpoints.Select(item => item with { IsBound = false, Message = null }).ToArray();
            _hasTemporaryBreakpoint = false;
        }
    }

    public void ForceTerminateOwnedProcesses()
    {
        int? pid;
        lock (_stateLock) pid = _processId;
        if (pid is not null)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid.Value);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        if (_transport is DbgXCommandTransport dbgX) dbgX.ForceTerminateOwnedProcesses();
    }

    internal void SetTestState(RocketDebugSessionState state, int? processId)
    {
        lock (_stateLock) { _state = state; _processId = processId; }
    }

    private static string? FindBreakpointError(string output)
    {
        var text = output.Trim();
        return text.Contains("Couldn't resolve", StringComparison.OrdinalIgnoreCase) || text.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("deferred bp", StringComparison.OrdinalIgnoreCase) || text.Contains("Syntax error", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Ambiguous", StringComparison.OrdinalIgnoreCase) || text.Contains("Unable to", StringComparison.OrdinalIgnoreCase) ? text : null;
    }

    private static void ValidateLaunchArtifacts(RocketDebugLaunchRequest request)
    {
        if (!File.Exists(request.ExecutablePath)) throw new FileNotFoundException("Rocket debug executable was not found.", request.ExecutablePath);
        if (!File.Exists(request.PdbPath)) throw new FileNotFoundException("Rocket debug PDB was not found.", request.PdbPath);
        if (!File.Exists(request.SourceMapPath)) throw new FileNotFoundException("Rocket debug source map was not found.", request.SourceMapPath);
        if (!Directory.Exists(request.SourceRoot)) throw new DirectoryNotFoundException($"Rocket debug source root was not found: {request.SourceRoot}");
        if (!Directory.Exists(request.WorkingDirectory)) throw new DirectoryNotFoundException($"Rocket debug working directory was not found: {request.WorkingDirectory}");
    }

    private static void ValidateBreakpointSources(IReadOnlyList<RocketDebugBreakpoint> breakpoints, RocketDebugSourceMap sourceMap)
    {
        foreach (var breakpoint in breakpoints)
        {
            if (!breakpoint.IsEnabled) continue;
            var mapped = sourceMap.TryResolveDebuggerSource(Path.GetFileName(breakpoint.SourcePath));
            if (mapped is null || !string.Equals(Path.GetFullPath(mapped), Path.GetFullPath(breakpoint.SourcePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Breakpoint source is not part of the active Rocket debug source map: {breakpoint.SourcePath}");
        }
    }

    private void EnsureStopped()
    {
        if (State != RocketDebugSessionState.Stopped || _stopRequested)
            throw new InvalidOperationException("The Rocket debug target must be stopped for this operation.");
    }

    private void SetState(RocketDebugSessionState state, string? message)
    {
        lock (_stateLock) _state = state;
        StateChanged?.Invoke(this, new(state, message));
    }
    private void Transport_OutputReceived(object? sender, RocketDebugOutputEventArgs e) => OutputReceived?.Invoke(this, e);
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
