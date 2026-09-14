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

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RocketDebugBreakpoint> Breakpoints { get; } = [];
    public ObservableCollection<RocketDebugThread> Threads { get; } = [];
    public ObservableCollection<RocketDebugStackFrame> Frames { get; } = [];
    public ObservableCollection<RocketDebugVariable> Locals { get; } = [];

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

    public bool IsActive => State is RocketDebugSessionState.Launching or RocketDebugSessionState.Running or RocketDebugSessionState.Stopped;
    public bool IsRunning => State == RocketDebugSessionState.Running;
    public bool IsStopped => State == RocketDebugSessionState.Stopped;

    public IReadOnlyList<RocketDebugBreakpoint> ToggleBreakpoint(string sourcePath, int line)
    {
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
        State = state;
        StatusText = string.IsNullOrWhiteSpace(message) ? $"Debugger: {state}" : message;
        if (state is RocketDebugSessionState.Terminated or RocketDebugSessionState.Idle or RocketDebugSessionState.Faulted)
        {
            Threads.Clear();
            Frames.Clear();
            Locals.Clear();
        }
    }

    public void ApplySnapshot(IRocketNativeDebugger debugger)
    {
        ArgumentNullException.ThrowIfNull(debugger);
        Replace(Breakpoints, debugger.Breakpoints);
        Replace(Threads, debugger.Threads);
        Replace(Frames, debugger.Frames);
        Replace(Locals, debugger.Locals);
        ApplyState(debugger.State, null);
    }

    public void ApplyBoundBreakpoints(IEnumerable<RocketDebugBreakpoint> breakpoints) => Replace(Breakpoints, breakpoints);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
