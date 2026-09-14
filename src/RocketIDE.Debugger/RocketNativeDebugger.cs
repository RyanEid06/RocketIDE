namespace RocketIDE.Debugger;

public sealed class RocketNativeDebugger : IRocketNativeDebugger
{
    private readonly IDebuggerCommandTransport _transport;
    private readonly object _stateLock = new();
    private RocketDebugSessionState _state = RocketDebugSessionState.Idle;
    private RocketDebugSourceMap? _sourceMap;
    private IReadOnlyList<RocketDebugBreakpoint> _breakpoints = [];
    private IReadOnlyList<RocketDebugThread> _threads = [];
    private IReadOnlyList<RocketDebugStackFrame> _frames = [];
    private IReadOnlyList<RocketDebugVariable> _locals = [];
    private RocketDebugStopLocation? _currentLocation;
    private int? _processId;
    private Task? _activeExecution;
    private bool _disposed;

    public RocketNativeDebugger(IDebuggerCommandTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
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

    public async Task LaunchAsync(RocketDebugLaunchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (State is not (RocketDebugSessionState.Idle or RocketDebugSessionState.Terminated or RocketDebugSessionState.Faulted))
        {
            throw new InvalidOperationException("A Rocket debug session is already active.");
        }

        ValidateLaunchArtifacts(request);
        var sourceMap = RocketDebugSourceMap.Read(request.SourceMapPath, request.SourceRoot);
        ValidateBreakpointSources(request.Breakpoints, sourceMap);
        SetState(RocketDebugSessionState.Launching, "Launching Rocket debug target…");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                _sourceMap = sourceMap;
                _breakpoints = request.Breakpoints.ToArray();
                _threads = [];
                _frames = [];
                _locals = [];
                _currentLocation = null;
                _processId = null;
            }

            await _transport.CreateProcessAsync(
                request.ExecutablePath,
                request.Arguments,
                request.WorkingDirectory,
                cancellationToken).ConfigureAwait(false);
            await ConfigureEngineAsync(sourceMap, cancellationToken).ConfigureAwait(false);
            var processText = await _transport.ExecuteAsync("|", cancellationToken).ConfigureAwait(false);
            var processId = DbgEngProtocol.ParseCurrentProcessId(processText)
                ?? throw new InvalidOperationException("DbgEng did not report a current debuggee process after launch.");
            lock (_stateLock) _processId = processId;
            await BindBreakpointsAsync(request.Breakpoints, cancellationToken).ConfigureAwait(false);
            SetState(RocketDebugSessionState.Stopped, "Rocket debug target created.");
            await ContinueAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TryStopTransportAsync().ConfigureAwait(false);
            SetState(RocketDebugSessionState.Terminated, "Rocket debug launch cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            await TryStopTransportAsync().ConfigureAwait(false);
            SetState(RocketDebugSessionState.Faulted, exception.Message);
            throw;
        }
    }

    public async Task SetBreakpointsAsync(IReadOnlyList<RocketDebugBreakpoint> breakpoints, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        ThrowIfDisposed();
        if (State == RocketDebugSessionState.Running)
        {
            throw new InvalidOperationException("Pause the Rocket debug target before changing live breakpoints.");
        }
        if (_sourceMap is null)
        {
            lock (_stateLock) _breakpoints = breakpoints.ToArray();
            return;
        }
        ValidateBreakpointSources(breakpoints, _sourceMap);
        await BindBreakpointsAsync(breakpoints, cancellationToken).ConfigureAwait(false);
    }

    public Task ContinueAsync(CancellationToken cancellationToken) => ExecuteRunCommandAsync("g", "continue", cancellationToken);

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (State != RocketDebugSessionState.Running) return;
        int processId;
        lock (_stateLock)
        {
            processId = _processId ?? throw new InvalidOperationException("The active debugger process ID is unavailable.");
        }
        await _transport.BreakAsync(processId, cancellationToken).ConfigureAwait(false);
    }

    public Task StepOverAsync(CancellationToken cancellationToken) => ExecuteRunCommandAsync("p", "step over", cancellationToken);
    public Task StepIntoAsync(CancellationToken cancellationToken) => ExecuteRunCommandAsync("t", "step into", cancellationToken);
    public Task StepOutAsync(CancellationToken cancellationToken) => ExecuteRunCommandAsync("gu", "step out", cancellationToken);

    public async Task SelectThreadAsync(int debuggerThreadIndex, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureStopped();
        await _transport.ExecuteAsync(DbgEngProtocol.BuildSelectThreadCommand(debuggerThreadIndex), cancellationToken).ConfigureAwait(false);
        await RefreshStoppedStateAsync("thread selection", cancellationToken).ConfigureAwait(false);
    }

    public async Task SelectFrameAsync(int frameIndex, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureStopped();
        await _transport.ExecuteAsync(DbgEngProtocol.BuildSelectFrameCommand(frameIndex), cancellationToken).ConfigureAwait(false);
        var localsText = await _transport.ExecuteAsync("dv /t", cancellationToken).ConfigureAwait(false);
        var locationText = await _transport.ExecuteAsync("ln @rip", cancellationToken).ConfigureAwait(false);
        RocketDebugStopLocation? location = _sourceMap is null
            ? null
            : DbgEngProtocol.ParseCurrentLocation(locationText, _sourceMap, "frame selection");
        if (location is null)
        {
            RocketDebugStackFrame? selected;
            lock (_stateLock) selected = _frames.FirstOrDefault(frame => frame.Index == frameIndex);
            if (selected?.SourcePath is not null && selected.Line is > 0)
            {
                location = new RocketDebugStopLocation(selected.SourcePath, selected.Line.Value, "frame selection");
            }
        }
        lock (_stateLock)
        {
            _locals = DbgEngProtocol.ParseLocals(localsText);
            _currentLocation = location;
            if (location is not null)
            {
                _frames = _frames.Select(frame => frame.Index == frameIndex
                    ? frame with { SourcePath = location.SourcePath, Line = location.Line }
                    : frame).ToArray();
            }
        }
        Stopped?.Invoke(this, new RocketDebugStoppedEventArgs(location));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var state = State;
        if (state is RocketDebugSessionState.Idle or RocketDebugSessionState.Terminated) return;
        if (state == RocketDebugSessionState.Running)
        {
            int? pid;
            lock (_stateLock) pid = _processId;
            if (pid is not null)
            {
                try { await _transport.BreakAsync(pid.Value, cancellationToken).ConfigureAwait(false); }
                catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
            }
            var active = _activeExecution;
            if (active is not null)
            {
                try { await active.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false); }
                catch (TimeoutException) { }
            }
        }
        await _transport.StopAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
            _processId = null;
            _threads = [];
            _frames = [];
            _locals = [];
            _currentLocation = null;
        }
        SetState(RocketDebugSessionState.Terminated, "Rocket debug session stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            if (State is not (RocketDebugSessionState.Idle or RocketDebugSessionState.Terminated))
            {
                try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
        }
        finally
        {
            _disposed = true;
            _transport.OutputReceived -= Transport_OutputReceived;
            await _transport.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }

    internal void SetTestState(RocketDebugSessionState state, int? processId)
    {
        lock (_stateLock)
        {
            _state = state;
            _processId = processId;
        }
    }

    private async Task ConfigureEngineAsync(RocketDebugSourceMap sourceMap, CancellationToken cancellationToken)
    {
        // Source-line breakpoint syntax uses the MASM expression evaluator in DbgEng.
        await _transport.ExecuteAsync(".expr /s masm", cancellationToken).ConfigureAwait(false);
        await _transport.ExecuteAsync(".lines -e", cancellationToken).ConfigureAwait(false);
        await _transport.ExecuteAsync("l+t", cancellationToken).ConfigureAwait(false);
        foreach (var directory in sourceMap.Sources.Select(Path.GetDirectoryName).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await _transport.ExecuteAsync(DbgEngProtocol.BuildSourcePathCommand(directory!), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task BindBreakpointsAsync(IReadOnlyList<RocketDebugBreakpoint> breakpoints, CancellationToken cancellationToken)
    {
        await _transport.ExecuteAsync("bc *", cancellationToken).ConfigureAwait(false);
        var bound = new List<RocketDebugBreakpoint>(breakpoints.Count);
        foreach (var breakpoint in breakpoints)
        {
            var basename = Path.GetFileName(breakpoint.SourcePath);
            var result = await _transport.ExecuteAsync(DbgEngProtocol.BuildSourceBreakpointCommand(basename, breakpoint.Line), cancellationToken).ConfigureAwait(false);
            var error = FindBreakpointError(result);
            bound.Add(breakpoint with
            {
                IsBound = error is null,
                Message = error,
            });
        }
        lock (_stateLock) _breakpoints = bound;
    }

    private async Task ExecuteRunCommandAsync(string command, string reason, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureStopped();
        cancellationToken.ThrowIfCancellationRequested();
        SetState(RocketDebugSessionState.Running, $"Rocket debugger: {reason}…");
        var execution = ExecuteAndRefreshAsync(command, reason);
        _activeExecution = execution;
        try
        {
            await execution.ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(_activeExecution, execution)) _activeExecution = null;
        }
    }

    private async Task ExecuteAndRefreshAsync(string command, string reason)
    {
        try
        {
            // Once target execution has been handed to DbgEng, cancellation must be expressed as
            // Pause/Stop. Abandoning the request would leave the native engine running out-of-band.
            await _transport.ExecuteAsync(command, CancellationToken.None).ConfigureAwait(false);
            var processText = await _transport.ExecuteAsync("|", CancellationToken.None).ConfigureAwait(false);
            var pid = DbgEngProtocol.ParseCurrentProcessId(processText);
            if (pid is null)
            {
                lock (_stateLock)
                {
                    _processId = null;
                    _threads = [];
                    _frames = [];
                    _locals = [];
                    _currentLocation = null;
                }
                SetState(RocketDebugSessionState.Terminated, "Rocket debug target exited.");
                return;
            }
            lock (_stateLock) _processId = pid;
            SetState(RocketDebugSessionState.Stopped, "Rocket debug target stopped.");
            await RefreshStoppedStateAsync(reason, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SetState(RocketDebugSessionState.Faulted, exception.Message);
            throw;
        }
    }

    private async Task RefreshStoppedStateAsync(string reason, CancellationToken cancellationToken)
    {
        var sourceMap = _sourceMap ?? throw new InvalidOperationException("Rocket debug source map is unavailable.");
        var threadsText = await _transport.ExecuteAsync("~", cancellationToken).ConfigureAwait(false);
        var framesText = await _transport.ExecuteAsync("kn", cancellationToken).ConfigureAwait(false);
        var localsText = await _transport.ExecuteAsync("dv /t", cancellationToken).ConfigureAwait(false);
        var locationText = await _transport.ExecuteAsync("ln @rip", cancellationToken).ConfigureAwait(false);
        var frames = DbgEngProtocol.ParseStackFrames(framesText, sourceMap).ToArray();
        var location = DbgEngProtocol.ParseCurrentLocation(locationText, sourceMap, reason);
        if (location is null)
        {
            var sourceFrame = frames.FirstOrDefault(frame => frame.SourcePath is not null && frame.Line is > 0);
            if (sourceFrame?.SourcePath is not null && sourceFrame.Line is > 0)
            {
                location = new RocketDebugStopLocation(sourceFrame.SourcePath, sourceFrame.Line.Value, reason);
            }
        }
        if (location is not null && frames.Length > 0 && frames[0].SourcePath is null)
        {
            frames[0] = frames[0] with { SourcePath = location.SourcePath, Line = location.Line };
        }
        lock (_stateLock)
        {
            _threads = DbgEngProtocol.ParseThreads(threadsText);
            _frames = frames;
            _locals = DbgEngProtocol.ParseLocals(localsText);
            _currentLocation = location;
        }
        Stopped?.Invoke(this, new RocketDebugStoppedEventArgs(location));
    }

    private static string? FindBreakpointError(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var text = output.Trim();
        return text.Contains("Couldn't resolve", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Syntax error", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Ambiguous", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Unable to", StringComparison.OrdinalIgnoreCase)
            ? text
            : null;
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
            var mapped = sourceMap.TryResolveDebuggerSource(Path.GetFileName(breakpoint.SourcePath));
            if (mapped is null || !string.Equals(Path.GetFullPath(mapped), Path.GetFullPath(breakpoint.SourcePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Breakpoint source is not part of the active Rocket debug source map: {breakpoint.SourcePath}");
            }
        }
    }

    private async Task TryStopTransportAsync()
    {
        try { await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { }
    }

    private void EnsureStopped()
    {
        if (State != RocketDebugSessionState.Stopped)
        {
            throw new InvalidOperationException("The Rocket debug target must be stopped for this operation.");
        }
    }

    private void SetState(RocketDebugSessionState state, string? message)
    {
        lock (_stateLock) _state = state;
        StateChanged?.Invoke(this, new RocketDebugStateChangedEventArgs(state, message));
    }

    private void Transport_OutputReceived(object? sender, RocketDebugOutputEventArgs e) => OutputReceived?.Invoke(this, e);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
