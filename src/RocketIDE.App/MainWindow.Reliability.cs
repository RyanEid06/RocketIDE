using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using RocketIDE.Core.Logging;
using RocketIDE.Core.Recovery;
using RocketIDE.Infrastructure.Recovery;

namespace RocketIDE.App;

public partial class MainWindow
{
    private readonly ISessionStore _sessionStore = JsonSessionStore.CreateDefault();
    private readonly IRecoveryStore _recoveryStore = JsonRecoveryStore.CreateDefault();
    private IApplicationLogger Logger => Application.Current is App app
        ? app.Logger
        : throw new InvalidOperationException("RocketIDE application logger is unavailable.");
    private DispatcherTimer? _recoveryTimer;
    private bool _reliabilityLoaded;
    private bool _recoveryNeedsDecision;

    private void InitializeReliability()
    {
        _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _recoveryTimer.Tick += async (_, _) => await SaveRecoverySnapshotAsync();
        Logger.Information("RocketIDE window initialized.");
    }

    private async Task LoadReliabilityAsync()
    {
        if (_reliabilityLoaded)
        {
            return;
        }

        _reliabilityLoaded = true;
        try
        {
            var session = await _sessionStore.LoadAsync(CancellationToken.None);
            ApplySessionState(session);
            await RestoreSessionDocumentsAsync(session);

            var recovery = await _recoveryStore.LoadAsync(CancellationToken.None);
            if (recovery.HasSnapshots)
            {
                var conflicts = recovery.Snapshots.Count(snapshot => snapshot.IsConflict);
                var suffix = conflicts == 0
                    ? ""
                    : $"\n\n{conflicts} file(s) changed on disk; RocketIDE will not overwrite them automatically.";
                var choice = MessageBox.Show(
                    this,
                    $"RocketIDE found {recovery.Snapshots.Count} unsaved recovery buffer(s). Restore them?{suffix}",
                    "Recover unsaved work",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (choice == MessageBoxResult.Yes)
                {
                    await RestoreRecoverySetAsync(recovery);
                }
                else if (choice == MessageBoxResult.No)
                {
                    await _recoveryStore.ClearAsync(CancellationToken.None);
                }
                else
                {
                    _recoveryNeedsDecision = true;
                }
            }

            await SaveSessionAsync(cleanShutdown: false);
            _recoveryTimer?.Start();
        }
        catch (Exception exception) when (IsExpectedReliabilityException(exception))
        {
            Logger.Warning("Session or recovery state could not be loaded.", exception);
            _viewModel.AppendOutput($"Reliability state unavailable: {exception.Message}");
        }
    }

    private async Task RestoreSessionDocumentsAsync(SessionState session)
    {
        var workspacePath = session.WorkspacePath;
        if (!string.IsNullOrWhiteSpace(workspacePath) && Directory.Exists(workspacePath))
        {
            await OpenWorkspaceAsync(workspacePath);
        }

        foreach (var path in session.OpenDocumentPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                await OpenDocumentAsync(path);
            }
        }

        if (!string.IsNullOrWhiteSpace(session.ActiveDocumentPath))
        {
            var active = FindOpenDocument(session.ActiveDocumentPath);
            if (active is not null)
            {
                _viewModel.ActiveDocument = active;
            }
        }
    }

    private async Task RestoreRecoverySetAsync(RecoverySet recovery)
    {
        var restored = new List<RecoverySnapshot>();
        foreach (var snapshot in recovery.Snapshots)
        {
            try
            {
                var tab = FindOpenDocument(snapshot.OriginalPath) ?? await OpenDocumentAsync(snapshot.OriginalPath);
                if (tab is null)
                {
                    restored.Add(snapshot);
                    continue;
                }

                var current = _documentStore.UpdateText(tab.Id, snapshot.Text);
                tab.UpdateSnapshot(current);
                _viewModel.ActiveDocument = tab;
            }
            catch (Exception exception) when (IsExpectedReliabilityException(exception))
            {
                restored.Add(snapshot);
                Logger.Warning($"Could not restore '{snapshot.OriginalPath}'.", exception);
            }
        }

        if (restored.Count == 0)
        {
            await _recoveryStore.ClearAsync(CancellationToken.None);
        }
        else
        {
            await _recoveryStore.SaveAsync(new RecoverySet(restored, recovery.CapturedUtc), CancellationToken.None);
        }
    }

    private async Task SaveRecoverySnapshotAsync()
    {
        if (!_reliabilityLoaded)
        {
            return;
        }

        try
        {
            var snapshots = new List<RecoverySnapshot>();
            foreach (var tab in _viewModel.Documents.Where(document => document.IsDirty))
            {
                var fingerprint = await JsonRecoveryStore.ComputeFingerprintAsync(tab.Path, CancellationToken.None);
                var lastWrite = File.Exists(tab.Path)
                    ? new DateTimeOffset(File.GetLastWriteTimeUtc(tab.Path), TimeSpan.Zero)
                    : DateTimeOffset.UnixEpoch;
                snapshots.Add(new RecoverySnapshot(tab.Path, fingerprint, lastWrite, tab.Version, tab.Text,
                    DateTimeOffset.UtcNow));
            }

            if (snapshots.Count == 0)
            {
                await _recoveryStore.ClearAsync(CancellationToken.None);
            }
            else
            {
                await _recoveryStore.SaveAsync(new RecoverySet(snapshots, DateTimeOffset.UtcNow), CancellationToken.None);
            }
        }
        catch (Exception exception) when (IsExpectedReliabilityException(exception))
        {
            Logger.Warning("Periodic recovery snapshot failed.", exception);
        }
    }

    private async Task SaveSessionAsync(bool cleanShutdown)
    {
        try
        {
            var session = new SessionState(
                _viewModel.Explorer.Workspace?.Path,
                _viewModel.Documents.Select(document => document.Path).ToArray(),
                _viewModel.ActiveDocument?.Path,
                CapturePanelLayout(),
                CaptureWindowBounds(),
                cleanShutdown,
                DateTimeOffset.UtcNow);
            await _sessionStore.SaveAsync(session, CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedReliabilityException(exception))
        {
            Logger.Warning("Session state could not be saved.", exception);
        }
    }

    private async Task CompleteReliabilityShutdownAsync()
    {
        _recoveryTimer?.Stop();
        try
        {
            await SaveSessionAsync(cleanShutdown: true);
            if (!_recoveryNeedsDecision)
            {
                await _recoveryStore.ClearAsync(CancellationToken.None);
            }
        }
        catch (Exception exception) when (IsExpectedReliabilityException(exception))
        {
            Logger.Warning("Clean-shutdown reliability cleanup failed.", exception);
        }
    }

    private void ApplySessionState(SessionState state)
    {
        if (state.SavedUtc == DateTimeOffset.UnixEpoch)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        var workArea = SystemParameters.WorkArea;
        var bounds = state.Window.ClampToWorkArea(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (bounds.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        var panels = state.Panels;
        ExplorerColumnDefinition.Width = panels.ExplorerVisible ? new GridLength(Math.Max(170, panels.ExplorerWidth)) : new GridLength(0);
        ExplorerSplitterColumnDefinition.Width = panels.ExplorerVisible ? new GridLength(5) : new GridLength(0);
        ExplorerPane.Visibility = panels.ExplorerVisible ? Visibility.Visible : Visibility.Collapsed;
        ExplorerSplitter.Visibility = panels.ExplorerVisible ? Visibility.Visible : Visibility.Collapsed;
        ExplorerViewMenu.IsChecked = panels.ExplorerVisible;
        BottomPanelRowDefinition.Height = panels.BottomPanelVisible ? new GridLength(Math.Max(100, panels.BottomPanelHeight)) : new GridLength(0);
        BottomPanelSplitter.Visibility = panels.BottomPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        BottomTabs.Visibility = panels.BottomPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        BottomPanelViewMenu.IsChecked = panels.BottomPanelVisible;
        if (panels.BottomTabIndex >= 0 && panels.BottomTabIndex < BottomTabs.Items.Count)
        {
            BottomTabs.SelectedIndex = panels.BottomTabIndex;
        }
    }

    private PanelLayout CapturePanelLayout() => new(
        ExplorerPane.Visibility == Visibility.Visible,
        BottomTabs.Visibility == Visibility.Visible,
        ExplorerColumnDefinition.Width.Value,
        BottomPanelRowDefinition.Height.Value,
        BottomTabs.SelectedIndex);

    private WindowBounds CaptureWindowBounds() => new(
        double.IsFinite(Left) ? Left : 100,
        double.IsFinite(Top) ? Top : 100,
        double.IsFinite(Width) ? Width : 1280,
        double.IsFinite(Height) ? Height : 800,
        WindowState == WindowState.Maximized);

    private void ExplorerViewMenu_Click(object sender, RoutedEventArgs e)
    {
        var visible = ExplorerViewMenu.IsChecked;
        ExplorerPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ExplorerSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ExplorerColumnDefinition.Width = visible ? new GridLength(Math.Max(170, ExplorerColumnDefinition.Width.Value)) : new GridLength(0);
        ExplorerSplitterColumnDefinition.Width = visible ? new GridLength(5) : new GridLength(0);
    }

    private void BottomPanelViewMenu_Click(object sender, RoutedEventArgs e)
    {
        var visible = BottomPanelViewMenu.IsChecked;
        BottomTabs.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BottomPanelSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BottomPanelRowDefinition.Height = visible ? new GridLength(Math.Max(100, BottomPanelRowDefinition.Height.Value)) : new GridLength(0);
    }

    private static bool IsExpectedReliabilityException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException;
}
