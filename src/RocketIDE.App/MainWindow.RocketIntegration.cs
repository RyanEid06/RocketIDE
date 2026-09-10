using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
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
    private readonly SemaphoreSlim _rocketIntegrationGate = new(1, 1);
    private readonly Dictionary<DocumentId, bool> _documentDirtyStates = new();
    private RocketToolSettings? _rocketToolSettings;
    private RocketLanguageClient? _languageClient;
    private DocumentSynchronizer? _documentSynchronizer;
    private string? _lspWorkspacePath;
    private string? _lastDiscoveryProblem;

    private void InitializeRocketIntegration()
    {
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
                MessageBox.Show(
                    this,
                    "Rocket environment validation found problems. Open the Output panel for the selected paths and details.",
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
            var dialog = new SettingsWindow(settings) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (dialog.Settings == RocketToolSettings.Automatic)
            {
                await _rocketToolSettingsStore.ResetAsync(CancellationToken.None);
            }
            else
            {
                await _rocketToolSettingsStore.SaveAsync(dialog.Settings, CancellationToken.None);
            }
            _rocketToolSettings = dialog.Settings;
            AppendRocketOutput("Rocket SDK settings updated. Tool discovery will use the new configuration.");
            await RestartLanguageServerAsync(CancellationToken.None);
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
        var locator = CreateToolLocator(settings);
        var activePath = GetRocketDiscoveryActivePath();
        var result = await new RocketEnvironmentValidator(locator).ValidateAsync(activePath, cancellationToken);

        AppendRocketOutput("=== Rocket environment validation ===");
        if (result.Toolchain is { } toolchain)
        {
            AppendRocketOutput($"Compiler: {toolchain.CompilerPath}");
            AppendRocketOutput($"Compiler version: {toolchain.CompilerVersion}");
            AppendRocketOutput($"Language server: {toolchain.LanguageServerPath}");
            AppendRocketOutput($"Language server version: {toolchain.LanguageServerVersion}");
            _viewModel.RocketSdkStatus = $"Rocket SDK: {toolchain.CompilerVersion}";
        }
        foreach (var problem in result.Problems)
        {
            AppendRocketOutput($"Problem: {problem}");
        }
        if (result.Problems.Count == 0)
        {
            AppendRocketOutput("Environment validation passed.");
        }
        return result;
    }

    private RocketToolLocator CreateToolLocator(RocketToolSettings settings) =>
        new(new RocketToolDiscoveryOptions(settings.CompilerPath, settings.LanguageServerPath, AppContext.BaseDirectory));

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
                    await CloseRocketDocumentAsync(tab.Path, CancellationToken.None);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (DocumentTabViewModel tab in e.NewItems)
                {
                    _documentDirtyStates[tab.Id] = tab.IsDirty;
                    tab.PropertyChanged += RocketDocument_PropertyChanged;
                    if (IsRocketPath(tab.Path))
                    {
                        await EnsureLanguageServerAsync(tab.Path, CancellationToken.None);
                        await OpenRocketDocumentAsync(tab, CancellationToken.None);
                    }
                }
            }
        }
        catch (Exception exception) when (IsExpectedRocketIntegrationException(exception))
        {
            AppendRocketOutput($"Rocket LSP document lifecycle failed: {exception.Message}");
            _viewModel.LspStatus = "LSP: offline";
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
                await ChangeRocketDocumentAsync(tab, CancellationToken.None);
            }
            else if (e.PropertyName == nameof(DocumentTabViewModel.IsDirty))
            {
                var wasDirty = _documentDirtyStates.GetValueOrDefault(tab.Id);
                _documentDirtyStates[tab.Id] = tab.IsDirty;
                if (wasDirty && !tab.IsDirty)
                {
                    await SaveRocketDocumentAsync(tab.Path, CancellationToken.None);
                }
            }
        }
        catch (Exception exception) when (IsExpectedRocketIntegrationException(exception))
        {
            AppendRocketOutput($"Rocket LSP document synchronization failed for '{tab.DisplayName}': {exception.Message}");
            _viewModel.LspStatus = "LSP: degraded";
        }
    }

    private async void Explorer_RocketIntegrationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RocketIDE.App.ViewModels.Explorer.WorkspaceExplorerViewModel.Workspace))
        {
            try
            {
                await RestartLanguageServerAsync(CancellationToken.None);
            }
            catch (Exception exception) when (IsExpectedRocketIntegrationException(exception))
            {
                AppendRocketOutput($"Rocket LSP workspace restart failed: {exception.Message}");
                _viewModel.LspStatus = "LSP: offline";
            }
        }
    }

    private async Task EnsureLanguageServerAsync(string? activePath, CancellationToken cancellationToken)
    {
        await _rocketIntegrationGate.WaitAsync(cancellationToken);
        try
        {
            var desiredWorkspace = GetLspWorkspacePath(activePath);
            if (_languageClient is { IsInitialized: true } &&
                string.Equals(_lspWorkspacePath, desiredWorkspace, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await StopLanguageServerCoreAsync(cancellationToken);
            await StartLanguageServerCoreAsync(activePath, desiredWorkspace, cancellationToken);
        }
        finally
        {
            _rocketIntegrationGate.Release();
        }
    }

    private async Task RestartLanguageServerAsync(CancellationToken cancellationToken)
    {
        await _rocketIntegrationGate.WaitAsync(cancellationToken);
        try
        {
            await StopLanguageServerCoreAsync(cancellationToken);
            var activePath = GetRocketDiscoveryActivePath();
            var workspace = GetLspWorkspacePath(activePath);
            if (workspace is not null)
            {
                await StartLanguageServerCoreAsync(activePath, workspace, cancellationToken);
            }
        }
        finally
        {
            _rocketIntegrationGate.Release();
        }
    }

    private async Task StartLanguageServerCoreAsync(string? activePath, string? workspacePath, CancellationToken cancellationToken)
    {
        if (workspacePath is null)
        {
            _viewModel.LspStatus = "LSP: offline";
            return;
        }

        var settings = await GetRocketToolSettingsAsync(cancellationToken);
        var discovery = await CreateToolLocator(settings).DiscoverAsync(activePath, cancellationToken);
        if (discovery.CompilerPath is not null && discovery.CompilerVersion is not null)
        {
            _viewModel.RocketSdkStatus = $"Rocket SDK: {discovery.CompilerVersion}";
        }
        else
        {
            _viewModel.RocketSdkStatus = "Rocket SDK: not found";
        }

        if (discovery.LanguageServerPath is null || discovery.LanguageServerVersion is null)
        {
            _viewModel.LspStatus = "LSP: offline";
            var problem = string.Join(" ", discovery.Problems);
            if (!string.Equals(_lastDiscoveryProblem, problem, StringComparison.Ordinal))
            {
                _lastDiscoveryProblem = problem;
                foreach (var item in discovery.Problems)
                {
                    AppendRocketOutput($"Rocket tool discovery: {item}");
                }
            }
            return;
        }

        var client = new RocketLanguageClient();
        client.LogReceived += LanguageClient_LogReceived;
        client.NotificationReceived += LanguageClient_NotificationReceived;
        try
        {
            _viewModel.LspStatus = "LSP: starting…";
            await client.StartAsync(discovery.LanguageServerPath, workspacePath, cancellationToken);
            _languageClient = client;
            _documentSynchronizer = new DocumentSynchronizer(client);
            _lspWorkspacePath = Path.GetFullPath(workspacePath);
            _lastDiscoveryProblem = null;
            _viewModel.LspStatus = $"LSP: online ({discovery.LanguageServerVersion})";
            AppendRocketOutput($"rocket-lsp initialized: {discovery.LanguageServerVersion}");
            AppendRocketOutput($"rocket-lsp path: {discovery.LanguageServerPath}");
            await SyncAllOpenRocketDocumentsCoreAsync(cancellationToken);
        }
        catch
        {
            client.LogReceived -= LanguageClient_LogReceived;
            client.NotificationReceived -= LanguageClient_NotificationReceived;
            await client.DisposeAsync();
            _viewModel.LspStatus = "LSP: offline";
            throw;
        }
    }

    private async Task StopLanguageServerCoreAsync(CancellationToken cancellationToken)
    {
        var client = _languageClient;
        _languageClient = null;
        _documentSynchronizer = null;
        _lspWorkspacePath = null;
        if (client is null)
        {
            return;
        }

        client.LogReceived -= LanguageClient_LogReceived;
        client.NotificationReceived -= LanguageClient_NotificationReceived;
        try
        {
            await client.StopAsync(cancellationToken);
        }
        finally
        {
            await client.DisposeAsync();
            _viewModel.LspStatus = "LSP: offline";
        }
    }

    private async Task SyncAllOpenRocketDocumentsCoreAsync(CancellationToken cancellationToken)
    {
        if (_documentSynchronizer is null)
        {
            return;
        }

        foreach (var tab in _viewModel.Documents.Where(tab => IsRocketPath(tab.Path)))
        {
            var state = await _documentSynchronizer.OpenAsync(tab.Path, tab.Text, tab.Version, cancellationToken);
            UpdateLargeFileStatus(tab, state);
        }
    }

    private async Task OpenRocketDocumentAsync(DocumentTabViewModel tab, CancellationToken cancellationToken)
    {
        await _rocketIntegrationGate.WaitAsync(cancellationToken);
        try
        {
            if (_documentSynchronizer is null)
            {
                return;
            }
            var state = await _documentSynchronizer.OpenAsync(tab.Path, tab.Text, tab.Version, cancellationToken);
            UpdateLargeFileStatus(tab, state);
        }
        finally
        {
            _rocketIntegrationGate.Release();
        }
    }

    private async Task ChangeRocketDocumentAsync(DocumentTabViewModel tab, CancellationToken cancellationToken)
    {
        await _rocketIntegrationGate.WaitAsync(cancellationToken);
        try
        {
            if (_documentSynchronizer is null)
            {
                return;
            }
            var state = await _documentSynchronizer.ChangeAsync(tab.Path, tab.Text, tab.Version, cancellationToken);
            UpdateLargeFileStatus(tab, state);
        }
        finally
        {
            _rocketIntegrationGate.Release();
        }
    }

    private async Task SaveRocketDocumentAsync(string path, CancellationToken cancellationToken)
    {
        await _rocketIntegrationGate.WaitAsync(cancellationToken);
        try
        {
            if (_documentSynchronizer is not null)
            {
                await _documentSynchronizer.SaveAsync(path, cancellationToken);
            }
        }
        finally
        {
            _rocketIntegrationGate.Release();
        }
    }

    private async Task CloseRocketDocumentAsync(string path, CancellationToken cancellationToken)
    {
        await _rocketIntegrationGate.WaitAsync(cancellationToken);
        try
        {
            if (_documentSynchronizer is not null)
            {
                await _documentSynchronizer.CloseAsync(path, cancellationToken);
            }
        }
        finally
        {
            _rocketIntegrationGate.Release();
        }
    }

    private async Task ShutdownRocketIntegrationAsync(CancellationToken cancellationToken)
    {
        await _rocketIntegrationGate.WaitAsync(cancellationToken);
        try
        {
            await StopLanguageServerCoreAsync(cancellationToken);
        }
        finally
        {
            _rocketIntegrationGate.Release();
        }
    }

    private void LanguageClient_LogReceived(object? sender, string line) => AppendRocketOutput($"[rocket-lsp] {line}");

    private void LanguageClient_NotificationReceived(object? sender, RocketServerNotificationEventArgs e)
    {
        if (!string.Equals(e.Method, "rocket/analysisStatus", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var status = e.Parameters.Deserialize<RocketAnalysisStatus>(LspJson.Options);
            if (status is null)
            {
                return;
            }
            _ = Dispatcher.InvokeAsync(() =>
                _viewModel.LspStatus = $"LSP: online · {status.Files} files · {status.ElapsedMilliseconds} ms");
        }
        catch (JsonException exception)
        {
            AppendRocketOutput($"Invalid rocket/analysisStatus payload: {exception.Message}");
        }
    }

    private void AppendRocketOutput(string line)
    {
        if (Dispatcher.CheckAccess())
        {
            _viewModel.AppendOutput(line);
        }
        else
        {
            _ = Dispatcher.InvokeAsync(() => _viewModel.AppendOutput(line));
        }
    }

    private void UpdateLargeFileStatus(DocumentTabViewModel tab, LspDocumentSyncState state)
    {
        if (state == LspDocumentSyncState.LargeFileUnsupportedByLsp)
        {
            _viewModel.LspStatus = $"LSP: large-file mode ({tab.DisplayName})";
            AppendRocketOutput($"LSP disabled for '{tab.DisplayName}': document exceeds Rocket's 4 MiB open-document limit.");
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
        LspProtocolException or JsonRpcResponseException or System.ComponentModel.Win32Exception;
}
