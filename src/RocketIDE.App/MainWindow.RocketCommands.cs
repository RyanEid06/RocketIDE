using System.ComponentModel;
using System.IO;
using System.Windows;
using RocketIDE.App.Editor;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Infrastructure.Processes;
using RocketIDE.Rocket.Compiler;

namespace RocketIDE.App;

public partial class MainWindow
{
    private readonly WindowsJobProcessRunner _rocketProcessRunner = new();
    private RocketCommandService? _activeRocketCommandService;
    private CancellationTokenSource? _rocketCommandCancellation;
    private int _rocketCommandRunning;

    private async void CheckRocket_Click(object sender, RoutedEventArgs e) =>
        await ExecuteRocketCommandAsync(RocketCommandKind.Check);

    private async void BuildRocket_Click(object sender, RoutedEventArgs e) =>
        await ExecuteRocketCommandAsync(RocketCommandKind.Build);

    private async void RunRocket_Click(object sender, RoutedEventArgs e) =>
        await ExecuteRocketCommandAsync(RocketCommandKind.Run);

    private async void TestRocket_Click(object sender, RoutedEventArgs e) =>
        await ExecuteRocketCommandAsync(RocketCommandKind.Test);

    private async void NewRocketProject_Click(object sender, RoutedEventArgs e)
    {
        var directory = GetSelectedDirectory() ?? _viewModel.Explorer.Workspace?.Path;
        if (directory is null)
        {
            AppendRocketOutput("Open a workspace before creating a Rocket project.");
            return;
        }

        var dialog = new NameInputDialog("New Rocket Project", "Project folder name:", "new-rocket-project") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var destination = _workspaceFileSystem.ResolveChildPath(directory, dialog.Value);
            await ExecuteAdvancedRocketCommandAsync(
                RocketAdvancedCommandKind.New,
                new RocketAdvancedCommandOptions(DestinationPath: destination));
        }
        catch (Exception exception) when (IsExpectedRocketCommandException(exception) || exception is ArgumentException)
        {
            AppendRocketOutput($"Rocket new failed: {exception.Message}");
        }
    }

    private async void ResolveDependencies_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Resolve, new RocketAdvancedCommandOptions());

    private async void ResolveDependenciesLocked_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Resolve, new RocketAdvancedCommandOptions(Locked: true));

    private async void ResolveDependenciesOffline_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Resolve, new RocketAdvancedCommandOptions(Offline: true));

    private async void DependencyTree_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Tree, new RocketAdvancedCommandOptions());

    private async void AuditDependencies_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Audit, new RocketAdvancedCommandOptions());

    private async void TargetInformation_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Target, new RocketAdvancedCommandOptions(Verbose: true));

    private async void FormatTarget_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Format, new RocketAdvancedCommandOptions());

    private async void Coverage_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Coverage, CreateMeasurementOptions("coverage"));

    private async void Profile_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Profile, CreateMeasurementOptions("profile"));

    private async void Benchmark_Click(object sender, RoutedEventArgs e) =>
        await ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Benchmark, CreateMeasurementOptions("benchmark"));

    private void StopRocket_Click(object sender, RoutedEventArgs e)
    {
        if (Volatile.Read(ref _rocketCommandRunning) == 0)
        {
            return;
        }

        AppendRocketOutput("Stopping active Rocket process tree…");
        _rocketCommandCancellation?.Cancel();
        _activeRocketCommandService?.StopActive();
    }

    private void ClearOutput_Click(object sender, RoutedEventArgs e) => _viewModel.Output.Clear();

    private void CopyOutputSelected_Click(object sender, RoutedEventArgs e) => CopyOutputLines(selectedOnly: true);

    private void CopyOutputAll_Click(object sender, RoutedEventArgs e) => CopyOutputLines(selectedOnly: false);

    private void OutputList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.C &&
            System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            CopyOutputLines(selectedOnly: OutputList.SelectedItems.Count > 0);
            e.Handled = true;
        }
    }

    private void CopyOutputLines(bool selectedOnly)
    {
        var lines = selectedOnly
            ? OutputList.SelectedItems.Cast<string>().ToArray()
            : _viewModel.Output.Lines.ToArray();
        if (lines.Length > 0)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }
    }

    private async Task ExecuteAdvancedRocketCommandAsync(
        RocketAdvancedCommandKind kind,
        RocketAdvancedCommandOptions options)
    {
        if (Interlocked.CompareExchange(ref _rocketCommandRunning, 1, 0) != 0)
        {
            AppendRocketOutput("A Rocket command is already running. Stop it before starting another command.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        _rocketCommandCancellation = cancellation;
        _viewModel.SetRocketCommandRunning(true);
        try
        {
            if (kind is (RocketAdvancedCommandKind.Coverage or RocketAdvancedCommandKind.Profile or RocketAdvancedCommandKind.Benchmark) &&
                MessageBox.Show(
                    this,
                    "This command may execute native Rocket code. Continue only if the active target is trusted.",
                    $"Rocket {kind}",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                AppendRocketOutput($"Rocket {kind} cancelled by the user.");
                return;
            }

            var activePath = kind == RocketAdvancedCommandKind.New
                ? GetRocketDiscoveryActivePath() ?? _viewModel.Explorer.Workspace?.Path
                : GetRocketCommandActivePath();
            if (activePath is null)
            {
                throw new InvalidOperationException("Open a Rocket workspace or target before using this command.");
            }

            if (kind != RocketAdvancedCommandKind.New && !await SaveDirtyDocumentsBeforeRocketCommandAsync())
            {
                AppendRocketOutput($"Rocket {kind} cancelled because an unsaved document could not be saved.");
                return;
            }

            var settings = await GetRocketToolSettingsAsync(cancellation.Token);
            ShowOutputPanel();
            _viewModel.Output.BeginCommand(kind.ToString(), options.DestinationPath ?? activePath);
            var service = new RocketCommandService(
                _rocketProcessRunner,
                CreateToolLocator(settings),
                _targetDiscovery);
            _activeRocketCommandService = service;
            var progress = new UiSynchronousProgress<RocketCommandOutput>(this, item => _viewModel.Output.Append(item.DisplayText));
            var result = await service.ExecuteAdvancedAsync(kind, activePath, options, progress, cancellation.Token);
            AppendRocketOutput(result.ProcessResult.Cancelled
                ? $"Rocket {kind} stopped."
                : $"Rocket {kind} exited with code {result.ProcessResult.ExitCode}.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            AppendRocketOutput($"Rocket {kind} stopped.");
        }
        catch (Exception exception) when (IsExpectedRocketCommandException(exception))
        {
            AppendRocketOutput($"Rocket {kind} failed: {exception.Message}");
            ShowOutputPanel();
        }
        finally
        {
            if (ReferenceEquals(_rocketCommandCancellation, cancellation))
            {
                _rocketCommandCancellation = null;
            }
            cancellation.Dispose();
            _activeRocketCommandService = null;
            _viewModel.SetRocketCommandRunning(false);
            Interlocked.Exchange(ref _rocketCommandRunning, 0);
            UpdateRocketCommandAvailability();
        }
    }

    private RocketAdvancedCommandOptions CreateMeasurementOptions(string name)
    {
        var activePath = GetRocketCommandActivePath();
        var target = activePath is null ? null : _targetDiscovery.Discover(activePath);
        if (target is null)
        {
            return new RocketAdvancedCommandOptions();
        }

        var directory = Path.Combine(target.WorkingDirectory, ".rocketc", "rocketide");
        Directory.CreateDirectory(directory);
        return new RocketAdvancedCommandOptions(OutputPath: Path.Combine(directory, $"{name}.json"));
    }

    private async Task ExecuteRocketCommandAsync(RocketCommandKind kind)
    {
        if (Interlocked.CompareExchange(ref _rocketCommandRunning, 1, 0) != 0)
        {
            AppendRocketOutput("A Rocket command is already running. Stop it before starting another command.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        _rocketCommandCancellation = cancellation;
        _viewModel.SetRocketCommandRunning(true);
        try
        {
            var activePath = GetRocketCommandActivePath();
            if (activePath is null)
            {
                throw new InvalidOperationException("Open a Rocket source file or a workspace containing rocket.toml before using Check, Build, Run, or Test.");
            }

            var target = _targetDiscovery.Discover(activePath)
                ?? throw new InvalidOperationException("The active file does not resolve to a Rocket target.");
            if (kind == RocketCommandKind.Run && !target.IsExecutable)
            {
                throw new InvalidOperationException($"Run is unavailable for Rocket {target.OutputKind} targets.");
            }

            if (!await SaveDirtyDocumentsBeforeRocketCommandAsync())
            {
                AppendRocketOutput($"Rocket {kind} cancelled because an unsaved document could not be saved.");
                return;
            }

            var settings = await GetRocketToolSettingsAsync(cancellation.Token);
            IReadOnlyList<string> programArguments = [];
            if (kind == RocketCommandKind.Run && !string.IsNullOrWhiteSpace(settings.ProgramArguments))
            {
                programArguments = WindowsCommandLine.ParseArguments(settings.ProgramArguments);
            }

            cancellation.Token.ThrowIfCancellationRequested();
            _viewModel.Problems.ClearCompilerDiagnostics();
            if (kind == RocketCommandKind.Test)
            {
                _viewModel.Tests.BeginRun();
                BottomTabs.SelectedIndex = 3;
            }
            else
            {
                ShowOutputPanel();
            }
            _viewModel.Output.BeginCommand(kind.ToString(), target.IsStandalone ? target.InputPath : target.WorkingDirectory);

            var diagnostics = new List<RocketDiagnostic>();
            var service = new RocketCommandService(
                _rocketProcessRunner,
                CreateToolLocator(settings),
                _targetDiscovery);
            _activeRocketCommandService = service;
            var progress = new UiSynchronousProgress<RocketCommandOutput>(this, item =>
                HandleRocketCommandOutput(item, kind, target, diagnostics));

            var result = await service.ExecuteAsync(
                kind,
                activePath,
                programArguments,
                progress,
                cancellation.Token);

            AppendRocketOutput(result.ProcessResult.Cancelled
                ? $"Rocket {kind} stopped."
                : $"Rocket {kind} exited with code {result.ProcessResult.ExitCode}.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            AppendRocketOutput($"Rocket {kind} stopped.");
        }
        catch (Exception exception) when (IsExpectedRocketCommandException(exception))
        {
            AppendRocketOutput($"Rocket {kind} failed: {exception.Message}");
            ShowOutputPanel();
        }
        finally
        {
            if (ReferenceEquals(_rocketCommandCancellation, cancellation))
            {
                _rocketCommandCancellation = null;
            }
            cancellation.Dispose();
            _activeRocketCommandService = null;
            _viewModel.SetRocketCommandRunning(false);
            Interlocked.Exchange(ref _rocketCommandRunning, 0);
            UpdateRocketCommandAvailability();
        }
    }

    private void HandleRocketCommandOutput(
        RocketCommandOutput output,
        RocketCommandKind kind,
        RocketIDE.Rocket.Projects.RocketTarget target,
        List<RocketDiagnostic> diagnostics)
    {
        _viewModel.Output.Append(output.DisplayText);
        if (output.Message is null)
        {
            return;
        }

        if (string.Equals(output.Message.Reason, "diagnostic", StringComparison.Ordinal) &&
            RocketMessageParser.TryMapDiagnostic(output.Message, target, out var diagnostic, kind.ToString()) &&
            diagnostic is not null)
        {
            diagnostics.Add(diagnostic);
            _viewModel.Problems.SetCompilerDiagnostics(diagnostics);
        }

        if (output.Message.Reason is "test-started" or "test-finished" or "test-summary")
        {
            _viewModel.Tests.Apply(output.Message);
        }
    }

    private async Task<bool> SaveDirtyDocumentsBeforeRocketCommandAsync()
    {
        foreach (var tab in _viewModel.Documents.Where(document => document.IsDirty).ToArray())
        {
            if (!await SaveTabAsync(tab))
            {
                return false;
            }
        }
        return true;
    }

    private string? GetRocketCommandActivePath()
    {
        if (_viewModel.ActiveDocument is { } active && _targetDiscovery.Discover(active.Path) is not null)
        {
            return active.Path;
        }

        if (_viewModel.Explorer.Workspace is { } workspace)
        {
            var manifest = Path.Combine(workspace.Path, "rocket.toml");
            if (File.Exists(manifest) && _targetDiscovery.Discover(manifest) is not null)
            {
                return manifest;
            }
        }

        return null;
    }

    private void UpdateRocketCommandAvailability()
    {
        var activePath = GetRocketCommandActivePath();
        var target = activePath is null ? null : _targetDiscovery.Discover(activePath);
        _viewModel.SetRocketCommandAvailability(target is not null, target?.IsExecutable == true);
    }

    private void ShutdownRocketCommands()
    {
        _rocketCommandCancellation?.Cancel();
        _activeRocketCommandService?.StopActive();
        _rocketProcessRunner.Dispose();
    }


    private sealed class UiSynchronousProgress<T>(MainWindow owner, Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            if (owner.Dispatcher.CheckAccess())
            {
                report(value);
                return;
            }

            owner.Dispatcher.Invoke(() => report(value));
        }
    }

    private static bool IsExpectedRocketCommandException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or
            Win32Exception or ArgumentException or PlatformNotSupportedException or ObjectDisposedException;
}
