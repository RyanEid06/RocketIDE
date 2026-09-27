using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using RocketIDE.App.Commands;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Debugger;
using RocketIDE.Infrastructure.Processes;
using RocketIDE.Infrastructure.Settings;
using RocketIDE.Rocket.Compiler;
using RocketIDE.Rocket.Debugger;

namespace RocketIDE.App;

public partial class MainWindow
{
    private IRocketNativeDebugger? _nativeDebugger;
    private CancellationTokenSource? _debugOperationCancellation;
    private DebugLaunchConfiguration? _lastDebugLaunch;

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
        catch (OperationCanceledException)
        {
            if (_nativeDebugger?.State != RocketDebugSessionState.Faulted)
                _viewModel.Debug.ApplyState(RocketDebugSessionState.Terminated, "Debugger: launch cancelled.");
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"Rocket debugger failed: {exception.Message}");
            _viewModel.Debug.ApplyState(RocketDebugSessionState.Faulted, $"Debugger: {exception.Message}");
            ShowDebugPanel();
        }
        finally
        {
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
        try
        {
            await StopDebugSessionAsync();
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
        var view = EditorContext.ActiveView;
        var document = view?.Document;
        var target = view?.CommandTarget;
        if (document is null || target is null || !string.Equals(Path.GetExtension(document.Path), ".rocket", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await ChangeDebugBreakpointsAsync(() => _viewModel.Debug.ToggleBreakpoint(document.Path, target.CaretLine));
    }

    private async void DebugThreads_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_nativeDebugger is null || !_viewModel.Debug.CanInspect || DebugThreadsList.SelectedItem is not RocketDebugThread thread) return;
        var revision = _viewModel.Debug.BeginInspection();
        try
        {
            await _nativeDebugger.SelectThreadAsync(thread.Index, CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"Select debugger thread failed: {exception.Message}");
        }
        finally { _viewModel.Debug.EndInspection(revision); }
    }

    private async void DebugFrames_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_nativeDebugger is null || !_viewModel.Debug.CanInspect || DebugFramesList.SelectedItem is not RocketDebugStackFrame frame) return;
        var revision = _viewModel.Debug.BeginInspection();
        try
        {
            await _nativeDebugger.SelectFrameAsync(frame.Index, CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"Select debugger frame failed: {exception.Message}");
        }
        finally { _viewModel.Debug.EndInspection(revision); }
    }

    private async Task StartDebugSessionAsync(DebugLaunchConfiguration? restart = null)
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
        _viewModel.Debug.ApplyState(RocketDebugSessionState.Launching, "Debugger: building target…");
        RocketDebugArtifactPaths? paths = null;
        IReadOnlyList<string> arguments = [];
        try
        {
            var activePath = restart?.InputPath ?? GetRocketCommandActivePath()
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

            var settings = restart?.Settings ?? await GetRocketToolSettingsAsync(cancellation.Token);
            _lastDebugLaunch = new DebugLaunchConfiguration(activePath, settings);
            _viewModel.SetHasDebugLaunch(true);
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
                _ = HandleRocketCommandOutputAsync(item, RocketCommandKind.DebugBuild, target, diagnostics);
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
            if (paths is null && _viewModel.Debug.State == RocketDebugSessionState.Launching)
                _viewModel.Debug.ApplyState(RocketDebugSessionState.Terminated, "Debugger: build did not launch a target.");
            if (paths is null && ReferenceEquals(_debugOperationCancellation, cancellation)) _debugOperationCancellation = null;
        }

        if (paths is null) return;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var debugger = await EnsureNativeDebuggerAsync(cancellation.Token);
            ShowDebugPanel();
            _outputBuffer.BeginCommand("Debug", paths.ExecutablePath);
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
        if (_nativeDebugger is null || !_viewModel.Debug.CanInspect) return;
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
        if (cancellationToken.IsCancellationRequested)
        {
            await transport.DisposeAsync();
            transport.ForceTerminateOwnedProcesses();
            cancellationToken.ThrowIfCancellationRequested();
        }
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
            if (!ReferenceEquals(sender, _nativeDebugger) || _lifetime.IsStopping || sender is not IRocketNativeDebugger debugger) return;
            // Queued native state events may be superseded before WPF processes them.
            if (e.State != debugger.State) return;
            _viewModel.Debug.ApplyState(debugger.State, e.Message);
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
            if (!ReferenceEquals(sender, _nativeDebugger) || _lifetime.IsStopping || sender is not IRocketNativeDebugger debugger || debugger.State != RocketDebugSessionState.Stopped) return;
            _viewModel.Debug.ApplySnapshot(debugger);
            RefreshDebugEditorPresentation();
            if (e.Location is not null) await NavigateToDebugLocationAsync(e.Location);
            ShowDebugPanel();
            await RefreshDebugWatchesAsync();
        });

    private async Task NavigateToDebugLocationAsync(RocketDebugStopLocation location)
    {
        if (!File.Exists(location.SourcePath)) return;
        RefreshDebugEditorPresentation();
        var zeroBasedLine = Math.Max(0, location.Line - 1);
        await EditorNavigation.OpenOrRevealAsync(
            location.SourcePath, new SourceRange(zeroBasedLine, 0, zeroBasedLine, 0), CancellationToken.None);
    }

    private void RefreshDebugEditorPresentation()
    {
        var current = _nativeDebugger?.State == RocketDebugSessionState.Stopped ? _nativeDebugger.CurrentLocation : null;
        foreach (var document in _viewModel.Documents)
        {
            var lines = _viewModel.Debug.GetBreakpointLines(document.Path);
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
            ModifierKeys.Control => $"Ctrl+{key}",
            ModifierKeys.Control | ModifierKeys.Shift => $"Ctrl+Shift+{key}",
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
            case RocketCommandRegistry.DebugRestart: DebugRestart_Click(this, new RoutedEventArgs()); break;
            case RocketCommandRegistry.DebugRunToCursor: DebugRunToCursor_Click(this, new RoutedEventArgs()); break;
            default: return false;
        }
        return true;
    }

    private static bool IsExpectedDebuggerException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            Win32Exception or COMException or TimeoutException or PlatformNotSupportedException or ObjectDisposedException or OperationCanceledException;
}
