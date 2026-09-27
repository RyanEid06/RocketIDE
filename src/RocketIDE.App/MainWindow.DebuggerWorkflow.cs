using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RocketIDE.Debugger;

namespace RocketIDE.App;

public partial class MainWindow
{
    private async Task StopDebugSessionAsync()
    {
        _debugOperationCancellation?.Cancel();
        _activeRocketCommandService?.StopActive();
        var debugger = _nativeDebugger;
        _viewModel.Debug.ApplyState(RocketDebugSessionState.Terminating, "Debugger: stopping…");
        if (debugger is not null)
        {
            try { await debugger.StopAsync(CancellationToken.None); }
            finally
            {
                DetachDebuggerEvents(debugger);
                if (ReferenceEquals(_nativeDebugger, debugger)) _nativeDebugger = null;
                await debugger.DisposeAsync();
            }
        }
        _viewModel.Debug.ApplyState(RocketDebugSessionState.Terminated, "Debugger: stopped");
        RefreshDebugEditorPresentation();
        UpdateRocketCommandAvailability();
    }

    private async void DebugRestart_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanDebugRestart || _lastDebugLaunch is null) return;
        var configuration = _lastDebugLaunch;
        try
        {
            await StopDebugSessionAsync();
            if (!_lifetime.IsStopping) await StartDebugSessionAsync(configuration);
        }
        catch (OperationCanceledException)
        {
            if (_nativeDebugger?.State != RocketDebugSessionState.Faulted)
                _viewModel.Debug.ApplyState(RocketDebugSessionState.Terminated, "Debugger: restart cancelled.");
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            AppendRocketOutput($"Restart debugging failed: {exception.Message}");
            _viewModel.Debug.ApplyState(RocketDebugSessionState.Faulted, $"Debugger: {exception.Message}");
        }
    }

    private async void DebugRunToCursor_Click(object sender, RoutedEventArgs e)
    {
        var view = EditorContext.ActiveView;
        if (!_viewModel.CanDebugRunToCursor || _nativeDebugger is null || view?.Document is null || view.CommandTarget is null) return;
        try { await _nativeDebugger.RunToCursorAsync(view.Document.Path, view.CommandTarget.CaretLine, CancellationToken.None); }
        catch (Exception exception) when (IsExpectedDebuggerException(exception)) { AppendRocketOutput($"Run to Cursor failed: {exception.Message}"); }
    }

    private async Task ChangeDebugBreakpointsAsync(Action change)
    {
        if (!_viewModel.Debug.CanEditBreakpoints) return;
        var before = _viewModel.Debug.Breakpoints.ToArray();
        long? revision = null;
        try
        {
            change();
            RefreshDebugEditorPresentation();
            if (_nativeDebugger?.State != RocketDebugSessionState.Stopped) return;
            revision = _viewModel.Debug.BeginInspection();
            await _nativeDebugger.SetBreakpointsAsync(_viewModel.Debug.Breakpoints.ToArray(), CancellationToken.None);
            _viewModel.Debug.ApplyBoundBreakpoints(_nativeDebugger.Breakpoints);
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception))
        {
            _viewModel.Debug.ApplyBoundBreakpoints(before.Select(item => item with { IsBound = false }));
            AppendRocketOutput($"Change breakpoint failed: {exception.Message}");
        }
        finally
        {
            if (revision is long value) _viewModel.Debug.EndInspection(value);
            RefreshDebugEditorPresentation();
        }
    }

    private async void DebugBreakpointEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: RocketDebugBreakpoint breakpoint } checkbox)
            await ChangeDebugBreakpointsAsync(() => _viewModel.Debug.SetBreakpointEnabled(breakpoint, checkbox.IsChecked == true));
    }
    private async void DebugRemoveBreakpoint_Click(object sender, RoutedEventArgs e)
    {
        if (DebugBreakpointsList.SelectedItem is RocketDebugBreakpoint breakpoint)
            await ChangeDebugBreakpointsAsync(() => _viewModel.Debug.RemoveBreakpoint(breakpoint));
    }
    private async void DebugRemoveAllBreakpoints_Click(object sender, RoutedEventArgs e) =>
        await ChangeDebugBreakpointsAsync(_viewModel.Debug.RemoveAllBreakpoints);
    private async void DebugBreakpointNavigate_Click(object sender, RoutedEventArgs e)
    {
        if (DebugBreakpointsList.SelectedItem is RocketDebugBreakpoint breakpoint)
            await NavigateToDebugLocationAsync(new(breakpoint.SourcePath, breakpoint.Line, "breakpoint navigation"));
    }
    private void DebugBreakpoints_MouseDoubleClick(object sender, MouseButtonEventArgs e) => DebugBreakpointNavigate_Click(sender, e);

    private async Task RefreshDebugWatchesAsync()
    {
        var debugger = _nativeDebugger;
        if (debugger?.State != RocketDebugSessionState.Stopped || !_viewModel.Debug.CanInspect || _viewModel.Debug.Watches.Count == 0) return;
        var expressions = _viewModel.Debug.Watches.Select(item => item.Expression).ToArray();
        var revision = _viewModel.Debug.BeginInspection();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var results = new List<RocketDebugEvaluation>();
        try
        {
            foreach (var expression in expressions)
            {
                if (debugger.State != RocketDebugSessionState.Stopped || deadline.IsCancellationRequested) break;
                results.Add(await debugger.EvaluateAsync(expression, deadline.Token));
            }
            if (ReferenceEquals(debugger, _nativeDebugger) && debugger.State == RocketDebugSessionState.Stopped)
                _viewModel.Debug.ApplyWatchResults(revision, results);
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception)) { AppendRocketOutput($"Watch refresh failed: {exception.Message}"); }
        finally { _viewModel.Debug.EndInspection(revision); }
    }
    private async void DebugAddWatch_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Debug.IsInspecting) return;
        if (!_viewModel.Debug.TryAddWatch(DebugExpressionInput.Text.Trim(), out var error)) { _viewModel.Debug.EvaluationText = error; return; }
        await RefreshDebugWatchesAsync();
    }
    private void DebugRemoveWatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Debug.IsInspecting && DebugWatchesList.SelectedItem is RocketDebugEvaluation watch) _viewModel.Debug.Watches.Remove(watch);
    }
    private async void DebugRefreshWatches_Click(object sender, RoutedEventArgs e) => await RefreshDebugWatchesAsync();
    private async void DebugEvaluate_Click(object sender, RoutedEventArgs e)
    {
        var debugger = _nativeDebugger;
        if (debugger is null || !_viewModel.Debug.CanInspect) return;
        var revision = _viewModel.Debug.BeginInspection();
        try
        {
            var result = await debugger.EvaluateAsync(DebugExpressionInput.Text.Trim(), CancellationToken.None);
            if (ReferenceEquals(debugger, _nativeDebugger) && debugger.State == RocketDebugSessionState.Stopped && _viewModel.Debug.IsCurrentInspection(revision))
                _viewModel.Debug.EvaluationText = result.Value;
        }
        catch (Exception exception) when (IsExpectedDebuggerException(exception)) { AppendRocketOutput($"Evaluate failed: {exception.Message}"); }
        finally { _viewModel.Debug.EndInspection(revision); }
    }
}
