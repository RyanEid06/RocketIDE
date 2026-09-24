using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using RocketIDE.App.Commands;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Debugger;
using RocketIDE.Infrastructure.Processes;
using RocketIDE.Rocket.Compiler;
using RocketIDE.Rocket.Debugger;

namespace RocketIDE.App;

public partial class MainWindow
{
    private IRocketNativeDebugger? _nativeDebugger;
    private CancellationTokenSource? _debugOperationCancellation;

    private async void DebugStartContinue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_nativeDebugger?.State == RocketDebugSessionState.Stopped)
            {
                await _nativeDebugger.ContinueAsync(CancellationToken.None);
                return;
            }

            if (_viewModel.Debug.IsActive)
            {
                return;
            }

            await StartDebugSessionAsync();
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"Rocket debugger failed: {exception.Message}");
            _viewModel.Debug.ApplyState(RocketDebugSessionState.Faulted, $"Debugger: {exception.Message}");
            ShowDebugPanel();
        }
        finally
        {
            _debugOperationCancellation = null;
            UpdateRocketCommandAvailability();
        }
    }

    private async void DebugPause_Click(object sender, RoutedEventArgs e)
    {
        if (_nativeDebugger is null || !_viewModel.Debug.IsRunning) return;
        try
        {
            await _nativeDebugger.PauseAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"Pause debugging failed: {exception.Message}");
        }
    }

    private async void DebugStop_Click(object sender, RoutedEventArgs e)
    {
        _debugOperationCancellation?.Cancel();
        _activeRocketCommandService?.StopActive();
        if (_nativeDebugger is null) return;
        try
        {
            await _nativeDebugger.StopAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"Stop debugging failed: {exception.Message}");
        }
    }

    private async void DebugStepOver_Click(object sender, RoutedEventArgs e) => await ExecuteDebugStepAsync("Step over", debugger => debugger.StepOverAsync(CancellationToken.None));
    private async void DebugStepInto_Click(object sender, RoutedEventArgs e) => await ExecuteDebugStepAsync("Step into", debugger => debugger.StepIntoAsync(CancellationToken.None));
    private async void DebugStepOut_Click(object sender, RoutedEventArgs e) => await ExecuteDebugStepAsync("Step out", debugger => debugger.StepOutAsync(CancellationToken.None));

    private async void DebugToggleBreakpoint_Click(object sender, RoutedEventArgs e)
    {
        var document = _viewModel.ActiveDocument;
        if (document is null || !string.Equals(Path.GetExtension(document.Path), ".rocket", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var before = _viewModel.Debug.Breakpoints.ToArray();
        var caretLine = _editorIntegration.ActiveView?.CommandTarget?.CaretLine ?? _viewModel.ActiveView?.CaretLine ?? 1;
        var updated = _viewModel.Debug.ToggleBreakpoint(document.Path, caretLine);
        RefreshDebugEditorPresentation();
        if (_nativeDebugger?.State != RocketDebugSessionState.Stopped)
        {
            return;
        }

        try
        {
            await _nativeDebugger.SetBreakpointsAsync(updated, CancellationToken.None);
            _viewModel.Debug.ApplyBoundBreakpoints(_nativeDebugger.Breakpoints);
            RefreshDebugEditorPresentation();
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            _viewModel.Debug.ApplyBoundBreakpoints(before);
            RefreshDebugEditorPresentation();
            AppendRocketOutput($"Toggle breakpoint failed: {exception.Message}");
        }
    }

    private async void DebugThreads_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_nativeDebugger is null || DebugThreadsList.SelectedItem is not RocketDebugThread thread) return;
        try
        {
            await _nativeDebugger.SelectThreadAsync(thread.Index, CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"Select debugger thread failed: {exception.Message}");
        }
    }

    private async void DebugFrames_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_nativeDebugger is null || DebugFramesList.SelectedItem is not RocketDebugStackFrame frame) return;
        try
        {
            await _nativeDebugger.SelectFrameAsync(frame.Index, CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"Select debugger frame failed: {exception.Message}");
        }
    }

    private async Task StartDebugSessionAsync()
    {
        if (_lifetime.IsStopping) return;
        if (Interlocked.CompareExchange(ref _rocketCommandRunning, 1, 0) != 0)
        {
            AppendRocketOutput("A Rocket command is already running. Stop it before starting the debugger.");
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _debugOperationCancellation = cancellation;
        _rocketCommandCancellation = cancellation;
        _viewModel.SetRocketCommandRunning(true);
        RocketDebugArtifactPaths? paths = null;
        IReadOnlyList<string> arguments = [];
        try
        {
            var activePath = GetRocketCommandActivePath()
                ?? throw new InvalidOperationException("Open a Rocket executable target before debugging.");
            var target = _targetDiscovery.Discover(activePath)
                ?? throw new InvalidOperationException("The active file does not resolve to a Rocket target.");
            if (!target.IsExecutable)
            {
                throw new InvalidOperationException($"Debugging is unavailable for Rocket {target.OutputKind} targets.");
            }
            if (!await SaveDirtyDocumentsBeforeRocketCommandAsync())
            {
                AppendRocketOutput("Rocket debugging cancelled because an unsaved document could not be saved.");
                return;
            }

            var settings = await GetRocketToolSettingsAsync(cancellation.Token);
            arguments = string.IsNullOrWhiteSpace(settings.ProgramArguments)
                ? []
                : WindowsCommandLine.ParseArguments(settings.ProgramArguments);
            ShowOutputPanel();
            _outputBuffer.BeginCommand("Debug Build", target.IsStandalone ? target.InputPath : target.WorkingDirectory);
            _viewModel.Problems.ClearCompilerDiagnostics();

            string? artifact = null;
            var diagnostics = new List<RocketDiagnostic>();
            var service = new RocketCommandService(_rocketProcessRunner, CreateToolLocator(settings), _targetDiscovery);
            _activeRocketCommandService = service;
            var progress = new UiBufferedProgress<RocketCommandOutput>(item =>
            {
                HandleRocketCommandOutput(item, RocketCommandKind.DebugBuild, target, diagnostics);
                if (item.Message?.Reason == "build-finished" && item.Message.Success != false && !string.IsNullOrWhiteSpace(item.Message.Artifact))
                {
                    artifact = item.Message.Artifact;
                }
            });
            var result = await service.ExecuteAsync(RocketCommandKind.DebugBuild, activePath, [], progress, cancellation.Token);
            if (result.ProcessResult.Cancelled)
            {
                AppendRocketOutput("Rocket debug build stopped.");
                return;
            }
            if (result.ProcessResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Rocket debug build exited with code {result.ProcessResult.ExitCode}.");
            }
            if (string.IsNullOrWhiteSpace(artifact))
            {
                throw new InvalidOperationException("Rocket debug build succeeded but did not report an executable artifact.");
            }

            paths = RocketDebugArtifactPaths.FromCompilerArtifact(result.Target, artifact);
            var validation = RocketDebugArtifactValidator.Validate(paths.ExecutablePath, paths.PdbPath, paths.SourceMapPath, paths.SourceRoot);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException($"Rocket debug artifact validation failed. {validation.Summary}");
            }
            AppendRocketOutput($"Debug artifacts: {validation.Summary}");
            _viewModel.Debug.ApplyState(RocketDebugSessionState.Launching, "Debugger: launching Rocket target…");
        }
        finally
        {
            _activeRocketCommandService = null;
            if (ReferenceEquals(_rocketCommandCancellation, cancellation)) _rocketCommandCancellation = null;
            _viewModel.SetRocketCommandRunning(false);
            Interlocked.Exchange(ref _rocketCommandRunning, 0);
            UpdateRocketCommandAvailability();
        }

        if (paths is null) return;
        cancellation.Token.ThrowIfCancellationRequested();
        var debugger = await EnsureNativeDebuggerAsync(cancellation.Token);
        ShowDebugPanel();
        _outputBuffer.BeginCommand("Debug", paths.ExecutablePath);
        try
        {
            await debugger.LaunchAsync(new RocketDebugLaunchRequest(
                paths.ExecutablePath,
                paths.PdbPath,
                paths.SourceMapPath,
                paths.SourceRoot,
                paths.WorkingDirectory,
                arguments,
                _viewModel.Debug.Breakpoints.ToArray()), cancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_debugOperationCancellation, cancellation)) _debugOperationCancellation = null;
        }
    }

    private async Task ExecuteDebugStepAsync(string operation, Func<IRocketNativeDebugger, Task> action)
    {
        if (_nativeDebugger is null || !_viewModel.Debug.IsStopped) return;
        try
        {
            await action(_nativeDebugger);
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"{operation} failed: {exception.Message}");
        }
    }

    private async Task<IRocketNativeDebugger> EnsureNativeDebuggerAsync(CancellationToken cancellationToken)
    {
        if (_nativeDebugger is not null && _nativeDebugger.State is not (RocketDebugSessionState.Terminated or RocketDebugSessionState.Faulted))
        {
            return _nativeDebugger;
        }
        if (_nativeDebugger is not null)
        {
            DetachDebuggerEvents(_nativeDebugger);
            await _nativeDebugger.DisposeAsync();
            _nativeDebugger = null;
        }

        var transport = await DbgXCommandTransport.CreateAsync(cancellationToken);
        var debugger = new RocketNativeDebugger(transport);
        debugger.StateChanged += Debugger_StateChanged;
        debugger.OutputReceived += Debugger_OutputReceived;
        debugger.Stopped += Debugger_Stopped;
        _nativeDebugger = debugger;
        return debugger;
    }

    private void Debugger_StateChanged(object? sender, RocketDebugStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (sender is IRocketNativeDebugger debugger) _viewModel.Debug.ApplySnapshot(debugger);
            _viewModel.Debug.ApplyState(e.State, e.Message);
            RefreshDebugEditorPresentation();
            UpdateRocketCommandAvailability();
        });

    private void Debugger_OutputReceived(object? sender, RocketDebugOutputEventArgs e)
    {
        var lines = e.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines) AppendRocketOutput($"[debug] {line}");
    }

    private void Debugger_Stopped(object? sender, RocketDebugStoppedEventArgs e) =>
        Dispatcher.BeginInvoke(async () =>
        {
            if (sender is IRocketNativeDebugger debugger) _viewModel.Debug.ApplySnapshot(debugger);
            RefreshDebugEditorPresentation();
            if (e.Location is not null) await NavigateToDebugLocationAsync(e.Location);
            ShowDebugPanel();
        });

    private async Task NavigateToDebugLocationAsync(RocketDebugStopLocation location)
    {
        if (!File.Exists(location.SourcePath)) return;
        RefreshDebugEditorPresentation();
        var zeroBasedLine = Math.Max(0, location.Line - 1);
        await _editorIntegration.OpenOrRevealAsync(
            location.SourcePath,
            new SourceRange(zeroBasedLine, 0, zeroBasedLine, 0),
            CancellationToken.None);
    }

    private void RefreshDebugEditorPresentation()
    {
        var current = _nativeDebugger?.CurrentLocation;
        foreach (var document in _viewModel.Documents)
        {
            var lines = _viewModel.Debug.Breakpoints
                .Where(item => string.Equals(item.SourcePath, document.Path, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Line)
                .ToArray();
            var currentLine = current is not null && string.Equals(current.SourcePath, document.Path, StringComparison.OrdinalIgnoreCase)
                ? current.Line
                : (int?)null;
            document.SetDebugMarkers(lines, currentLine);
        }
    }

    private void ShowDebugPanel() => ShowBottomPanelTab(5);

    private async Task ShutdownDebuggerAsync(CancellationToken cancellationToken)
    {
        _debugOperationCancellation?.Cancel();
        var debugger = _nativeDebugger;
        _nativeDebugger = null;
        if (debugger is null) return;
        DetachDebuggerEvents(debugger);
        try { await debugger.StopAsync(cancellationToken).WaitAsync(cancellationToken); }
        finally { await debugger.DisposeAsync().AsTask().WaitAsync(cancellationToken); }
        _viewModel.Debug.ApplyState(RocketDebugSessionState.Terminated, "Debugger: stopped");
        RefreshDebugEditorPresentation();
    }

    private void DetachDebuggerEvents(IRocketNativeDebugger debugger)
    {
        debugger.StateChanged -= Debugger_StateChanged;
        debugger.OutputReceived -= Debugger_OutputReceived;
        debugger.Stopped -= Debugger_Stopped;
    }

    private bool TryHandleDebuggerGesture(Key key, ModifierKeys modifiers)
    {
        var gesture = modifiers switch
        {
            ModifierKeys.None => key.ToString(),
            ModifierKeys.Shift => $"Shift+{key}",
            _ => string.Empty,
        };
        if (string.IsNullOrEmpty(gesture)) return false;
        var command = _viewModel.CommandRegistry.FindByGesture(gesture);
        if (command is null || !_viewModel.GetCommandState(command.Id).IsEnabled) return command is not null;

        switch (command.Id)
        {
            case RocketCommandRegistry.DebugStartContinue: DebugStartContinue_Click(this, new RoutedEventArgs()); break;
            case RocketCommandRegistry.DebugPause: DebugPause_Click(this, new RoutedEventArgs()); break;
            case RocketCommandRegistry.DebugStop: DebugStop_Click(this, new RoutedEventArgs()); break;
            case RocketCommandRegistry.DebugToggleBreakpoint: DebugToggleBreakpoint_Click(this, new RoutedEventArgs()); break;
            case RocketCommandRegistry.DebugStepOver: DebugStepOver_Click(this, new RoutedEventArgs()); break;
            case RocketCommandRegistry.DebugStepInto: DebugStepInto_Click(this, new RoutedEventArgs()); break;
            case RocketCommandRegistry.DebugStepOut: DebugStepOut_Click(this, new RoutedEventArgs()); break;
            default: return false;
        }
        return true;
    }

    private static bool IsExpectedDebuggerException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            Win32Exception or COMException or TimeoutException or PlatformNotSupportedException or ObjectDisposedException or OperationCanceledException;
}
