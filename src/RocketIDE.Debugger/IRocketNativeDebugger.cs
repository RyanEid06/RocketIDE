namespace RocketIDE.Debugger;

public interface IRocketNativeDebugger : IAsyncDisposable
{
    event EventHandler<RocketDebugStateChangedEventArgs>? StateChanged;
    event EventHandler<RocketDebugOutputEventArgs>? OutputReceived;
    event EventHandler<RocketDebugStoppedEventArgs>? Stopped;

    RocketDebugSessionState State { get; }
    IReadOnlyList<RocketDebugBreakpoint> Breakpoints { get; }
    IReadOnlyList<RocketDebugThread> Threads { get; }
    IReadOnlyList<RocketDebugStackFrame> Frames { get; }
    IReadOnlyList<RocketDebugVariable> Locals { get; }
    RocketDebugStopLocation? CurrentLocation { get; }

    Task LaunchAsync(RocketDebugLaunchRequest request, CancellationToken cancellationToken);
    Task SetBreakpointsAsync(IReadOnlyList<RocketDebugBreakpoint> breakpoints, CancellationToken cancellationToken);
    Task ContinueAsync(CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task StepOverAsync(CancellationToken cancellationToken);
    Task StepIntoAsync(CancellationToken cancellationToken);
    Task StepOutAsync(CancellationToken cancellationToken);
    Task SelectThreadAsync(int debuggerThreadIndex, CancellationToken cancellationToken);
    Task SelectFrameAsync(int frameIndex, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
