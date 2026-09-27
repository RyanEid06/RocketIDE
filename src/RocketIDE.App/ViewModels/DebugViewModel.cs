using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.Debugger;

namespace RocketIDE.App.ViewModels;

public sealed class DebugViewModel : INotifyPropertyChanged
{
    private RocketDebugSessionState _state = RocketDebugSessionState.Idle;
    private string _statusText = "Debugger: idle";
    private bool _isInspecting;
    private long _inspectionRevision;
    private string _evaluationText = "Enter a local or parameter identifier.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RocketDebugBreakpoint> Breakpoints { get; } = [];
    public ObservableCollection<RocketDebugThread> Threads { get; } = [];
    public ObservableCollection<RocketDebugStackFrame> Frames { get; } = [];
    public ObservableCollection<RocketDebugVariable> Locals { get; } = [];
    public ObservableCollection<RocketDebugEvaluation> Watches { get; } = [];
    public bool IsInspecting
    {
        get => _isInspecting;
        private set { _isInspecting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInspect)); OnPropertyChanged(nameof(CanEditBreakpoints)); }
    }
    public bool CanInspect => IsStopped && !IsInspecting;
    public bool CanEditBreakpoints => !IsInspecting && State is RocketDebugSessionState.Idle or RocketDebugSessionState.Stopped or RocketDebugSessionState.Terminated or RocketDebugSessionState.Faulted;
    public string EvaluationText { get => _evaluationText; set { _evaluationText = value; OnPropertyChanged(); } }

    public long BeginInspection() { IsInspecting = true; return ++_inspectionRevision; }
    public void EndInspection(long revision) { if (revision == _inspectionRevision) IsInspecting = false; }
    public bool IsCurrentInspection(long revision) => IsStopped && revision == _inspectionRevision;
    public void InvalidateInspection()
    {
        ++_inspectionRevision;
        IsInspecting = false;
        EvaluationText = "Unavailable until evaluated in the current stopped frame.";
        Replace(Watches, Watches.Select(item => item with { Value = "Unavailable until refreshed in a stopped frame.", IsAvailable = false }).ToArray());
    }

    public bool TryAddWatch(string expression, out string error)
    {
        if (!DbgEngProtocol.IsSupportedExpression(expression)) error = "Use a local or parameter identifier, up to 128 characters. Members, operators and calls are unavailable.";
        else if (Watches.Any(item => item.Expression == expression)) error = "That identifier is already watched.";
        else if (Watches.Count >= 16) error = "The watch limit is 16 identifiers.";
        else { Watches.Add(new(expression, "Unavailable until stopped.", false)); error = string.Empty; return true; }
        return false;
    }

    public bool ApplyWatchResults(long revision, IEnumerable<RocketDebugEvaluation> values)
    {
        if (!IsCurrentInspection(revision)) return false;
        var results = values.ToDictionary(item => item.Expression, StringComparer.Ordinal);
        for (var i = 0; i < Watches.Count; i++)
            if (results.TryGetValue(Watches[i].Expression, out var result)) Watches[i] = result;
        return true;
    }

    public RocketDebugSessionState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStopped));
            OnPropertyChanged(nameof(CanInspect));
            OnPropertyChanged(nameof(CanEditBreakpoints));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (string.Equals(_statusText, value, StringComparison.Ordinal)) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsActive => State is RocketDebugSessionState.Launching or RocketDebugSessionState.Running or RocketDebugSessionState.Stopped or RocketDebugSessionState.Terminating;
    public bool IsRunning => State == RocketDebugSessionState.Running;
    public bool IsStopped => State == RocketDebugSessionState.Stopped;

    public IReadOnlyList<RocketDebugBreakpoint> ToggleBreakpoint(string sourcePath, int line)
    {
        if (!CanEditBreakpoints) throw new InvalidOperationException("Wait until the debugger is stopped before changing breakpoints.");
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!Path.IsPathFullyQualified(sourcePath)) throw new ArgumentException("Breakpoint source paths must be absolute.", nameof(sourcePath));
        if (line < 1) throw new ArgumentOutOfRangeException(nameof(line));
        sourcePath = Path.GetFullPath(sourcePath);

        var existing = Breakpoints.FirstOrDefault(item =>
            item.Line == line && string.Equals(item.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Breakpoints.Add(new RocketDebugBreakpoint(sourcePath, line));
        }
        else
        {
            Breakpoints.Remove(existing);
        }
        return Breakpoints.ToArray();
    }

    public void ApplyState(RocketDebugSessionState state, string? message)
    {
        if (State != state) InvalidateInspection();
        State = state;
        StatusText = string.IsNullOrWhiteSpace(message) ? $"Debugger: {state}" : message;
        if (state != RocketDebugSessionState.Stopped)
        {
            Threads.Clear();
            Frames.Clear();
            Locals.Clear();
        }
        if (state is RocketDebugSessionState.Terminated or RocketDebugSessionState.Idle or RocketDebugSessionState.Faulted)
            ApplyBoundBreakpoints(Breakpoints.Select(item => item with { IsBound = false, Message = null }).ToArray());
    }

    public void ApplySnapshot(IRocketNativeDebugger debugger)
    {
        ArgumentNullException.ThrowIfNull(debugger);
        InvalidateInspection();
        Replace(Breakpoints, debugger.Breakpoints);
        Replace(Threads, debugger.Threads);
        Replace(Frames, debugger.Frames);
        Replace(Locals, debugger.Locals);
        ApplyState(debugger.State, null);
    }

    public void ApplyBoundBreakpoints(IEnumerable<RocketDebugBreakpoint> breakpoints) => Replace(Breakpoints, breakpoints);

    public IReadOnlyList<int> GetBreakpointLines(string sourcePath) => Breakpoints
        .Where(item => item.IsEnabled && string.Equals(item.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)).Select(item => item.Line).ToArray();

    public void SetBreakpointEnabled(RocketDebugBreakpoint breakpoint, bool enabled)
    {
        if (!CanEditBreakpoints) throw new InvalidOperationException("Pause before changing breakpoints.");
        var index = Breakpoints.IndexOf(breakpoint);
        if (index >= 0) Breakpoints[index] = breakpoint with { IsEnabled = enabled, IsBound = false, Message = null };
    }

    public void RemoveBreakpoint(RocketDebugBreakpoint breakpoint)
    {
        if (!CanEditBreakpoints) throw new InvalidOperationException("Pause before changing breakpoints.");
        Breakpoints.Remove(breakpoint);
    }

    public void RemoveAllBreakpoints()
    {
        if (!CanEditBreakpoints) throw new InvalidOperationException("Pause before changing breakpoints.");
        Breakpoints.Clear();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
