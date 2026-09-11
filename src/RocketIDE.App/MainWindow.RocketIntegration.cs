using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.App.Views;
using RocketIDE.Core.Documents;
using RocketIDE.Infrastructure.Settings;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.LspDtos;
using RocketIDE.Rocket.Tools;

namespace RocketIDE.App;

public partial class MainWindow
{
    private readonly RocketToolSettingsStore _rocketToolSettingsStore = RocketToolSettingsStore.CreateDefault();
    private readonly Dictionary<DocumentId, bool> _documentDirtyStates = new();
    private RocketToolSettings? _rocketToolSettings;
    private RocketSessionCoordinator _rocketSession = null!;

    private void InitializeRocketIntegration()
    {
        _rocketSession = new RocketSessionCoordinator(
            GetRocketToolSettingsAsync,
            CreateToolLocator,
            static () => new RocketLanguageClient(),
            GetOpenRocketSessionDocuments,
            SetRocketSdkStatus,
            SetLspStatus,
            AppendRocketOutput,
            ShowOutputPanel);
        _rocketSession.NotificationReceived += RocketSession_NotificationReceived;
        _rocketSession.DiagnosticSessionChanged += RocketSession_DiagnosticSessionChanged;
        _rocketSession.DiagnosticsPublished += RocketSession_DiagnosticsPublished;
        _rocketSession.DocumentSyncStateChanged += RocketSession_DocumentSyncStateChanged;
        _viewModel.Documents.CollectionChanged += Documents_CollectionChanged;
        _viewModel.Explorer.PropertyChanged += Explorer_RocketIntegrationPropertyChanged;
    }

    private async void ValidateRocketEnvironment_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var validation = await ValidateRocketEnvironmentAsync(CancellationToken.None);
            if (validation.Toolchain is { } toolchain)
            {
                MessageBox.Show(
                    this,
                    $"Rocket environment is valid.\n\nCompiler: {toolchain.CompilerVersion}\nLanguage server: {toolchain.LanguageServerVersion}\n\nFull details were written to Output.",
                    "Rocket Environment",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                ShowOutputPanel();
                MessageBox.Show(
                    this,
                    "Rocket environment validation found problems. The Output panel was selected with the chosen paths and details.",
                    "Rocket Environment",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception) when (IsExpectedRocketIntegrationException(exception))
        {
            AppendRocketOutput($"Environment validation failed: {exception.Message}");
            MessageBox.Show(this, exception.Message, "Rocket Environment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RocketSdkSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await GetRocketToolSettingsAsync(CancellationToken.None);
            var dialog = new SettingsWindow(settings, _viewModel.Explorer.Workspace?.Path) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var updated = dialog.Settings;
            if (updated.IsAutomatic)
            {
                await _rocketToolSettingsStore.ResetAsync(CancellationToken.None);
            }
            else
            {
                await _rocketToolSettingsStore.SaveAsync(updated, CancellationToken.None);
            }

            _rocketToolSettings = updated;
            AppendRocketOutput("Rocket SDK settings updated. Tool discovery will use the new configuration.");
            var activePath = GetRocketDiscoveryActivePath();
            await _rocketSession.RestartAsync(activePath, GetLspWorkspacePath(activePath), CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedRocketIntegrationException(exception) || IsExpectedFileException(exception))
        {
            AppendRocketOutput($"Rocket SDK settings failed: {exception.Message}");
            MessageBox.Show(this, exception.Message, "Rocket SDK Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<RocketEnvironmentValidationResult> ValidateRocketEnvironmentAsync(CancellationToken cancellationToken)
    {
        var settings = await GetRocketToolSettingsAsync(cancellationToken);
        var activePath = GetRocketDiscoveryActivePath();
        var result = await new RocketEnvironmentValidator(CreateToolLocator(settings)).ValidateAsync(activePath, cancellationToken);

        AppendRocketOutput("=== Rocket environment validation ===");
        if (result.Toolchain is { } toolchain)
        {
            AppendRocketOutput($"Compiler: {toolchain.CompilerPath}");
            AppendRocketOutput($"Compiler version: {toolchain.CompilerVersion}");
            AppendRocketOutput($"Language server: {toolchain.LanguageServerPath}");
            AppendRocketOutput($"Language server version: {toolchain.LanguageServerVersion}");
            SetRocketSdkStatus($"Rocket SDK: {toolchain.CompilerVersion}");
        }
        else
        {
            SetRocketSdkStatus("Rocket SDK: not found");
        }

        foreach (var problem in result.Problems)
        {
            AppendRocketOutput($"Problem: {problem}");
        }

        if (result.Problems.Count == 0)
        {
            AppendRocketOutput("Environment validation passed.");
        }
        else
        {
            ShowOutputPanel();
        }

        return result;
    }

    private IRocketToolLocator CreateToolLocator(RocketToolSettings settings) =>
        new RocketToolLocator(new RocketToolDiscoveryOptions(
            settings.CompilerPath,
            settings.LanguageServerPath,
            AppContext.BaseDirectory,
            settings.TrustedCheckoutRoots));

    private async Task<RocketToolSettings> GetRocketToolSettingsAsync(CancellationToken cancellationToken)
    {
        if (_rocketToolSettings is not null)
        {
            return _rocketToolSettings;
        }

        _rocketToolSettings = await _rocketToolSettingsStore.LoadAsync(cancellationToken);
        return _rocketToolSettings;
    }

    private async void Documents_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            if (e.OldItems is not null)
            {
                foreach (DocumentTabViewModel tab in e.OldItems)
                {
                    tab.PropertyChanged -= RocketDocument_PropertyChanged;
                    _documentDirtyStates.Remove(tab.Id);
                    await _rocketSession.CloseDocumentAsync(tab.Path, CancellationToken.None);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (DocumentTabViewModel tab in e.NewItems)
                {
                    _documentDirtyStates[tab.Id] = tab.IsDirty;
                    tab.PropertyChanged += RocketDocument_PropertyChanged;
                    if (!IsRocketPath(tab.Path))
                    {
                        continue;
                    }

                    await _rocketSession.EnsureAsync(tab.Path, GetLspWorkspacePath(tab.Path), CancellationToken.None);
                    await _rocketSession.OpenDocumentAsync(ToSessionDocument(tab), CancellationToken.None);
                }
            }
        }
        catch (Exception exception) when (IsExpectedRocketIntegrationException(exception))
        {
            AppendRocketOutput($"Rocket LSP document lifecycle failed: {exception.Message}");
            SetLspStatus("LSP: offline");
        }
    }

    private async void RocketDocument_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentTabViewModel tab || !IsRocketPath(tab.Path))
        {
            return;
        }

        try
        {
            if (e.PropertyName == nameof(DocumentTabViewModel.Version))
            {
                await _rocketSession.ChangeDocumentAsync(ToSessionDocument(tab), CancellationToken.None);
            }
            else if (e.PropertyName == nameof(DocumentTabViewModel.IsDirty))
            {
                var wasDirty = _documentDirtyStates.GetValueOrDefault(tab.Id);
                _documentDirtyStates[tab.Id] = tab.IsDirty;
                if (wasDirty && !tab.IsDirty)
                {
                    await _rocketSession.SaveDocumentAsync(tab.Path, CancellationToken.None);
                }
            }
        }
        catch (Exception exception) when (IsExpectedRocketIntegrationException(exception))
        {
            AppendRocketOutput($"Rocket LSP document synchronization failed for '{tab.DisplayName}': {exception.Message}");
            SetLspStatus("LSP: degraded");
        }
    }

    private async void Explorer_RocketIntegrationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RocketIDE.App.ViewModels.Explorer.WorkspaceExplorerViewModel.Workspace))
        {
            return;
        }

        try
        {
            var activePath = GetRocketDiscoveryActivePath();
            await _rocketSession.RestartAsync(activePath, GetLspWorkspacePath(activePath), CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedRocketIntegrationException(exception))
        {
            AppendRocketOutput($"Rocket LSP workspace restart failed: {exception.Message}");
            SetLspStatus("LSP: offline");
        }
    }

    private async Task ShutdownRocketIntegrationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _rocketSession.ShutdownAsync(cancellationToken);
        }
        finally
        {
            _rocketSession.NotificationReceived -= RocketSession_NotificationReceived;
            _rocketSession.DiagnosticSessionChanged -= RocketSession_DiagnosticSessionChanged;
            _rocketSession.DiagnosticsPublished -= RocketSession_DiagnosticsPublished;
            _rocketSession.DocumentSyncStateChanged -= RocketSession_DocumentSyncStateChanged;
            _viewModel.Documents.CollectionChanged -= Documents_CollectionChanged;
            _viewModel.Explorer.PropertyChanged -= Explorer_RocketIntegrationPropertyChanged;
            foreach (var tab in _viewModel.Documents)
            {
                tab.PropertyChanged -= RocketDocument_PropertyChanged;
            }
        }
    }

    private void RocketSession_DiagnosticSessionChanged(object? sender, RocketDiagnosticSessionChangedEventArgs e) =>
        DispatchUi(() => _viewModel.BeginDiagnosticSession(e.Generation, e.IsOnline));

    private void RocketSession_DiagnosticsPublished(object? sender, RocketDiagnosticsPublishedEventArgs e) =>
        DispatchUi(() => _viewModel.ApplyDiagnosticPublication(e.Publication));

    private void RocketSession_DocumentSyncStateChanged(object? sender, RocketDocumentSyncStateChangedEventArgs e) =>
        DispatchUi(() => _viewModel.SetDocumentDiagnosticSupport(
            e.Path,
            e.Version,
            e.State != LspDocumentSyncState.LargeFileUnsupportedByLsp));

    private void RocketSession_NotificationReceived(object? sender, RocketServerNotificationEventArgs e)
    {
        if (!string.Equals(e.Method, "rocket/analysisStatus", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var status = e.Parameters.Deserialize<RocketAnalysisStatus>(LspJson.Options);
            if (status is not null)
            {
                SetLspStatus($"LSP: online · {status.Files} files · {status.ElapsedMilliseconds} ms");
            }
        }
        catch (JsonException exception)
        {
            AppendRocketOutput($"Invalid rocket/analysisStatus payload: {exception.Message}");
        }
    }

    private IReadOnlyList<RocketSessionDocument> GetOpenRocketSessionDocuments() =>
        _viewModel.Documents
            .Where(tab => IsRocketPath(tab.Path))
            .Select(ToSessionDocument)
            .ToArray();

    private static RocketSessionDocument ToSessionDocument(DocumentTabViewModel tab) =>
        new(tab.Path, tab.Text, tab.Version, tab.DisplayName);

    private void SetRocketSdkStatus(string status) => DispatchUi(() => _viewModel.RocketSdkStatus = status);

    private void SetLspStatus(string status) => DispatchUi(() => _viewModel.LspStatus = status);

    private void AppendRocketOutput(string line) => DispatchUi(() => _viewModel.AppendOutput(line));

    private void ShowOutputPanel() => DispatchUi(() => BottomTabs.SelectedIndex = 1);

    private void DispatchUi(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            _ = Dispatcher.InvokeAsync(action);
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
        }
    }

    private string? GetRocketDiscoveryActivePath()
    {
        if (_viewModel.Explorer.Workspace is { } workspace)
        {
            return workspace.Path;
        }

        if (_viewModel.ActiveDocument is { } active && IsRocketPath(active.Path))
        {
            return active.Path;
        }

        return _viewModel.Documents.FirstOrDefault(tab => IsRocketPath(tab.Path))?.Path;
    }

    private string? GetLspWorkspacePath(string? activePath)
    {
        if (_viewModel.Explorer.Workspace is { } workspace)
        {
            return workspace.Path;
        }

        if (string.IsNullOrWhiteSpace(activePath))
        {
            return null;
        }

        var full = Path.GetFullPath(activePath);
        return Directory.Exists(full) ? full : Path.GetDirectoryName(full);
    }

    private static bool IsRocketPath(string path) =>
        string.Equals(Path.GetExtension(path), ".rocket", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedRocketIntegrationException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or
        LspProtocolException or JsonRpcResponseException or ObjectDisposedException or System.ComponentModel.Win32Exception;
}
