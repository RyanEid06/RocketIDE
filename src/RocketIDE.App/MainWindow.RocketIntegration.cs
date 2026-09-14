using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using RocketIDE.App.Editor;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.App.Views;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Core.Documents;
using RocketIDE.Infrastructure.Settings;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;
using RocketIDE.Rocket.Tools;

namespace RocketIDE.App;

public partial class MainWindow
{
    private readonly RocketToolSettingsStore _rocketToolSettingsStore = RocketToolSettingsStore.CreateDefault();
    private readonly Dictionary<DocumentId, bool> _documentDirtyStates = new();
    private RocketToolSettings? _rocketToolSettings;
    private RocketSessionCoordinator _rocketSession = null!;
    private WorkspaceEditTransactionService _workspaceEdits = null!;
    private DocumentChangeScheduler? _documentChangeScheduler;
    private CancellationTokenSource? _wp09RequestCancellation;

    public IRocketEditorFeatureService RocketEditorFeatures => _rocketSession;

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
        _workspaceEdits = WorkspaceEditTransactionService.CreateFileSystemService(GetOpenWorkspaceEditDocuments);
        _documentChangeScheduler = new DocumentChangeScheduler(
            (document, cancellationToken) => _rocketSession.ChangeDocumentAsync(document, cancellationToken),
            onError: exception => AppendRocketOutput($"Rocket LSP document synchronization failed: {exception.Message}"));
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
                    _documentChangeScheduler?.Cancel(tab.Path);
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
                _wp09RequestCancellation?.Cancel();
                _documentChangeScheduler?.Schedule(ToSessionDocument(tab));
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
            UpdateActiveTargetStatus();
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
        CancelWp09Request();
        ShutdownRocketCommands();
        try
        {
            if (_documentChangeScheduler is not null)
            {
                await _documentChangeScheduler.DisposeAsync();
                _documentChangeScheduler = null;
            }
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


    private void GotoDefinition_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.RequestRocketCommand(RocketEditorCommand.Definition);
    private void FindReferences_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.RequestRocketCommand(RocketEditorCommand.References);
    private void RenameSymbol_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.RequestRocketCommand(RocketEditorCommand.Rename);
    private void CodeActions_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.RequestRocketCommand(RocketEditorCommand.CodeActions);
    private void FormatDocument_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.RequestRocketCommand(RocketEditorCommand.FormatDocument);

    private async void EditorHost_RocketCommandRequested(object? sender, RocketEditorCommandRequestedEventArgs e)
    {
        using var cancellation = BeginWp09Request();
        try
        {
            switch (e.Command)
            {
                case RocketEditorCommand.Definition:
                    await GoToDefinitionAsync(e.Path, e.Position, cancellation.Token);
                    break;
                case RocketEditorCommand.References:
                    await FindReferencesAsync(e.Path, e.Position, cancellation.Token);
                    break;
                case RocketEditorCommand.Rename:
                    await RenameSymbolAsync(e.Path, e.Position, cancellation.Token);
                    break;
                case RocketEditorCommand.CodeActions:
                    await ShowCodeActionsAsync(sender as EditorDocumentHost, e.Path, e.Range, cancellation.Token);
                    break;
                case RocketEditorCommand.FormatDocument:
                    await FormatDocumentAsync(e.Path, cancellation.Token);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedWp09Exception(exception))
        {
            AppendRocketOutput($"Rocket editor command failed: {exception.Message}");
            SetLspStatus("LSP: action failed");
        }
        finally
        {
            Interlocked.CompareExchange(ref _wp09RequestCancellation, null, cancellation);
        }
    }

    private async Task GoToDefinitionAsync(string path, LspPosition position, CancellationToken cancellationToken)
    {
        var version = FindOpenDocument(path)?.Version;
        var locations = await _rocketSession.RequestDefinitionAsync(path, position, cancellationToken);
        if (!IsSameDocumentVersion(path, version) || locations is null)
        {
            return;
        }
        if (locations.Count == 0)
        {
            SetLspStatus("LSP: no definition");
            return;
        }
        if (locations.Count == 1)
        {
            await NavigateToRocketLocationAsync(locations[0]);
            return;
        }

        _viewModel.References.SetResults("Definitions", locations);
        BottomTabs.SelectedIndex = 1;
    }

    private async Task FindReferencesAsync(string path, LspPosition position, CancellationToken cancellationToken)
    {
        var version = FindOpenDocument(path)?.Version;
        var locations = await _rocketSession.RequestReferencesAsync(path, position, cancellationToken);
        if (!IsSameDocumentVersion(path, version) || locations is null)
        {
            return;
        }
        _viewModel.References.SetResults("References", locations);
        BottomTabs.SelectedIndex = 1;
    }

    private async Task RenameSymbolAsync(string path, LspPosition position, CancellationToken cancellationToken)
    {
        var workflow = new RocketRenameWorkflow(_rocketSession);
        var result = await workflow.ExecuteAsync(
            path,
            position,
            async initialValue =>
            {
                var value = await Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new NameInputDialog("Rename Symbol", "New symbol name:", initialValue ?? string.Empty) { Owner = this };
                    return dialog.ShowDialog() == true ? dialog.Value : null;
                });
                return value;
            },
            edit => ApplyWorkspaceEditAsync(edit, path, cancellationToken),
            cancellationToken);

        switch (result.Status)
        {
            case RocketRenameWorkflowStatus.NotRenameable:
                SetLspStatus("LSP: symbol is not renameable");
                break;
            case RocketRenameWorkflowStatus.NoEdit:
                SetLspStatus("LSP: rename returned no edit");
                break;
            case RocketRenameWorkflowStatus.Applied:
                SetLspStatus("LSP: rename applied");
                break;
        }
    }

    private async Task ShowCodeActionsAsync(EditorDocumentHost? editorHost, string path, LspRange range, CancellationToken cancellationToken)
    {
        var tab = FindOpenDocument(path);
        if (tab is null)
        {
            return;
        }
        var version = tab.Version;
        var diagnostics = tab.Diagnostics.Select(MapCodeActionDiagnostic).ToArray();
        var context = RocketCodeActionContextBuilder.Build(range, diagnostics);
        var actions = await _rocketSession.RequestCodeActionsAsync(path, context.Range, context.Diagnostics, cancellationToken);
        if (!IsSameDocumentVersion(path, version) || actions is null)
        {
            return;
        }
        if (actions.Count == 0)
        {
            SetLspStatus("LSP: no server-provided code actions");
            return;
        }

        var menu = new ContextMenu { PlacementTarget = editorHost ?? GetActiveEditor() };
        foreach (var action in actions)
        {
            var unsupported = action.HasUnsupportedCommand || action.Edit is null;
            var disabled = !string.IsNullOrWhiteSpace(action.DisabledReason);
            var item = new MenuItem
            {
                Style = FindResource("IDE.MenuItemStyle") as Style,
                Header = disabled
                    ? $"{action.Title} — rocket-lsp ({action.DisabledReason})"
                    : unsupported
                        ? $"{action.Title} — rocket-lsp (unsupported in WP09)"
                        : $"{action.Title} — rocket-lsp",
                IsEnabled = !unsupported && !disabled,
            };
            if (!unsupported && !disabled)
            {
                item.Click += async (_, _) =>
                {
                    try
                    {
                        var status = await RocketCodeActionExecutor.TryApplyAsync(
                            action,
                            () => IsSameDocumentVersion(path, version),
                            edit => ApplyWorkspaceEditAsync(edit, path, CancellationToken.None));
                        SetLspStatus(status switch
                        {
                            RocketCodeActionApplyStatus.Applied => "LSP: quick fix applied",
                            RocketCodeActionApplyStatus.Stale => "LSP: code action is stale",
                            RocketCodeActionApplyStatus.Disabled => "LSP: code action disabled by server",
                            _ => "LSP: code action unsupported",
                        });
                    }
                    catch (Exception exception) when (IsExpectedWp09Exception(exception))
                    {
                        AppendRocketOutput($"Rocket code action failed: {exception.Message}");
                        SetLspStatus("LSP: quick fix failed");
                    }
                };
            }
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private async Task FormatDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var tab = FindOpenDocument(path);
        if (tab is null)
        {
            return;
        }
        var version = tab.Version;
        var edits = await _rocketSession.RequestFormattingAsync(path, 4, true, cancellationToken);
        if (!IsSameDocumentVersion(path, version) || edits is null)
        {
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var workspaceEdit = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(path, null, edits)]);
        var result = await ApplyWorkspaceEditAsync(workspaceEdit, path, cancellationToken);
        SetLspStatus(result.ChangedDocumentCount == 0 ? "LSP: document already formatted" : "LSP: document formatted");
    }

    private async Task<WorkspaceEditApplyResult> ApplyWorkspaceEditAsync(
        RocketWorkspaceEdit edit,
        string activePath,
        CancellationToken cancellationToken)
    {
        var result = await _workspaceEdits.ApplyAsync(edit, GetLspWorkspacePath(activePath), cancellationToken);
        if (result.ChangedDocumentCount > 0)
        {
            AppendRocketOutput($"Applied server WorkspaceEdit to {result.ChangedDocumentCount} document(s).");
        }
        return result;
    }

    private IReadOnlyList<WorkspaceEditOpenDocument> GetOpenWorkspaceEditDocuments() =>
        _viewModel.Documents.Select(tab => new WorkspaceEditOpenDocument(
            tab.Path,
            tab.Text,
            tab.Version,
            text => ApplyOpenWorkspaceEditText(tab, text))).ToArray();

    private void ApplyOpenWorkspaceEditText(DocumentTabViewModel tab, string text)
    {
        void Apply()
        {
            if (string.Equals(tab.EditorDocument.Text, text, StringComparison.Ordinal))
            {
                return;
            }
            using (tab.EditorDocument.RunUpdate())
            {
                tab.EditorDocument.Replace(0, tab.EditorDocument.TextLength, text);
            }
        }

        if (Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.Invoke(Apply);
        }
    }

    private async void ReferencesPanel_ReferenceInvoked(object? sender, ReferenceInvokedEventArgs e)
    {
        try
        {
            await NavigateToRocketLocationAsync(new RocketLocation(e.Item.FilePath, e.Item.Range));
        }
        catch (Exception exception) when (IsExpectedWp09Exception(exception))
        {
            AppendRocketOutput($"Rocket reference navigation failed: {exception.Message}");
            SetLspStatus("LSP: navigation failed");
        }
    }

    private async Task NavigateToRocketLocationAsync(RocketLocation location)
    {
        string text;
        var open = FindOpenDocument(location.Path);
        if (open is not null)
        {
            text = open.Text;
        }
        else
        {
            text = await ReadNavigationTextAsync(location.Path, CancellationToken.None);
        }

        if (!WorkspaceEditValidator.IsValidRange(text, location))
        {
            AppendRocketOutput($"Rejected rocket-lsp navigation target with invalid UTF-16 range: {location.Path}");
            SetLspStatus("LSP: invalid navigation target");
            return;
        }

        var tab = open ?? await OpenDocumentAsync(location.Path);
        if (tab is null)
        {
            return;
        }
        _viewModel.ActiveDocument = tab;
        tab.RequestNavigation(new SourceRange(
            location.Range.Start.Line,
            location.Range.Start.Character,
            location.Range.End.Line,
            location.Range.End.Character));
    }

    private static async Task<string> ReadNavigationTextAsync(string path, CancellationToken cancellationToken)
    {
        var normalized = Path.GetFullPath(path);
        if (!File.Exists(normalized))
        {
            throw new IOException($"Navigation target '{normalized}' does not exist.");
        }
        var bytes = await File.ReadAllBytesAsync(normalized, cancellationToken);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            throw new IOException($"Navigation target '{normalized}' is not a text file.");
        }
        var bom = Encoding.UTF8.GetPreamble();
        var offset = bytes.AsSpan().StartsWith(bom) ? bom.Length : 0;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new IOException($"Navigation target '{normalized}' is not valid UTF-8.", exception);
        }
    }

    private CancellationTokenSource BeginWp09Request()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _wp09RequestCancellation, cancellation);
        if (previous is not null)
        {
            previous.Cancel();
        }
        return cancellation;
    }

    private void CancelWp09Request()
    {
        var cancellation = Interlocked.Exchange(ref _wp09RequestCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private bool IsSameDocumentVersion(string path, int? expectedVersion)
    {
        if (!expectedVersion.HasValue)
        {
            return true;
        }
        return FindOpenDocument(path)?.Version == expectedVersion.Value;
    }

    private static RocketCodeActionDiagnostic MapCodeActionDiagnostic(RocketDiagnostic diagnostic) =>
        new(
            new LspRange(
                new LspPosition(diagnostic.Range.StartLine, diagnostic.Range.StartCharacter),
                new LspPosition(diagnostic.Range.EndLine, diagnostic.Range.EndCharacter)),
            diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => 1,
                DiagnosticSeverity.Warning => 2,
                DiagnosticSeverity.Information => 3,
                DiagnosticSeverity.Hint => 4,
                _ => null,
            },
            diagnostic.Code,
            diagnostic.Source,
            diagnostic.Message,
            diagnostic.Data);

    private static bool IsExpectedWp09Exception(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or LspProtocolException or
            JsonRpcResponseException or WorkspaceEditValidationException or WorkspaceEditCommitException;

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
        if (string.Equals(e.Method, "rocket/analysisStatus", StringComparison.Ordinal))
        {
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
            return;
        }

        if (!string.Equals(e.Method, "rocket/projectStatus", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var status = e.Parameters.Deserialize<RocketProjectStatus>(LspJson.Options);
            if (status is not null)
            {
                var overFiles = status.Files > status.MaximumProjectFiles;
                var overBytes = status.Bytes > status.MaximumProjectBytes;
                if (overFiles || overBytes)
                {
                    SetLspStatus($"LSP: project limit · {status.Files}/{status.MaximumProjectFiles} files · {status.Bytes}/{status.MaximumProjectBytes} bytes");
                    AppendRocketOutput("The active project exceeds Rocket LSP bounds; semantic responses may be incomplete.");
                }
                else
                {
                    SetLspStatus($"LSP: project · {status.Files} files · {status.Symbols} symbols");
                }
            }
        }
        catch (JsonException exception)
        {
            AppendRocketOutput($"Invalid rocket/projectStatus payload: {exception.Message}");
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

    private void ShowOutputPanel() => DispatchUi(() => BottomTabs.SelectedIndex = 2);

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
