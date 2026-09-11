using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RocketIDE.App.Editor;
using RocketIDE.App.Interop;
using RocketIDE.App.ViewModels;
using RocketIDE.App.ViewModels.Explorer;
using RocketIDE.Core.Documents;
using RocketIDE.Core.Workspaces;
using RocketIDE.Infrastructure.Files;
using RocketIDE.Infrastructure.Settings;
using RocketIDE.Rocket.Projects;

namespace RocketIDE.App;

public partial class MainWindow : Window
{
    private readonly FileDocumentStore _documentStore = new();
    private readonly IWorkspaceFileSystem _workspaceFileSystem = new WorkspaceFileSystem();
    private readonly RocketTargetDiscovery _targetDiscovery = new();
    private readonly RecentWorkspaceStore _recentWorkspaceStore = RecentWorkspaceStore.CreateDefault();
    private readonly MainWindowViewModel _viewModel;
    private readonly Dictionary<string, DateTime> _suppressedWorkspaceChanges = new(StringComparer.OrdinalIgnoreCase);
    private WorkspaceFileWatcher? _workspaceWatcher;
    private ExplorerNodeViewModel? _selectedExplorerNode;
    private bool _allowWindowClose;
    private bool _closePreparationInProgress;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(_workspaceFileSystem);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = _viewModel;
        InitializeRocketIntegration();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var recent = await _recentWorkspaceStore.LoadAsync(CancellationToken.None);
            _viewModel.SetRecentWorkspaces(recent);
        }
        catch (Exception exception) when (IsExpectedFileException(exception))
        {
            Trace.TraceWarning($"RocketIDE could not load recent projects: {exception.Message}");
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e) => WindowsTitleBar.ApplyDarkMode(this);

    private async void OpenFile_Click(object sender, RoutedEventArgs e) => await OpenFilesAsync();

    private async Task OpenFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open file",
            Filter = "Rocket source (*.rocket)|*.rocket|Text files (*.txt;*.md;*.toml;*.json)|*.txt;*.md;*.toml;*.json|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            await OpenDocumentAsync(path);
        }
    }

    private async Task OpenDocumentAsync(string path)
    {
        try
        {
            var snapshot = await _documentStore.OpenAsync(path, CancellationToken.None);
            _viewModel.AddOrActivate(_documentStore, snapshot);
        }
        catch (Exception exception) when (IsExpectedFileException(exception))
        {
            ShowFileError("Open failed", path, exception);
        }
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Open Rocket project or folder",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await OpenWorkspaceAsync(dialog.FolderName);
        }
    }

    private async Task OpenWorkspaceAsync(string path)
    {
        WorkspaceFileWatcher? candidateWatcher = null;
        try
        {
            candidateWatcher = new WorkspaceFileWatcher(path);
            candidateWatcher.Start();
            await _viewModel.Explorer.OpenAsync(path, CancellationToken.None);

            var previousWatcher = _workspaceWatcher;
            _workspaceWatcher = candidateWatcher;
            candidateWatcher = null;
            _workspaceWatcher.ChangesAvailable += WorkspaceWatcher_ChangesAvailable;
            DisposeWorkspaceWatcher(previousWatcher);

            _viewModel.AddRecentWorkspace(path);
            await PersistRecentWorkspacesAsync();
        }
        catch (Exception exception) when (IsExpectedFileException(exception) || exception is ArgumentException)
        {
            candidateWatcher?.Dispose();
            ShowFileError("Open folder failed", path, exception);
        }
    }


    private async Task PersistRecentWorkspacesAsync()
    {
        try
        {
            await _recentWorkspaceStore.SaveAsync(_viewModel.RecentWorkspaces, CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedFileException(exception))
        {
            Trace.TraceWarning($"RocketIDE could not save recent projects: {exception.Message}");
        }
    }

    private void RecentProjects_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        RecentProjectsMenu.Items.Clear();
        if (_viewModel.RecentWorkspaces.Count == 0)
        {
            RecentProjectsMenu.Items.Add(new MenuItem { Header = "No recent projects", IsEnabled = false });
            return;
        }

        foreach (var path in _viewModel.RecentWorkspaces)
        {
            var item = new MenuItem { Header = path, ToolTip = path };
            item.Click += async (_, _) => await OpenWorkspaceAsync(path);
            RecentProjectsMenu.Items.Add(item);
        }
    }

    private async void NewFile_Click(object sender, RoutedEventArgs e)
    {
        var directory = GetSelectedDirectory();
        string? path;

        if (directory is null)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Create Rocket file",
                FileName = "untitled.rocket",
                DefaultExt = ".rocket",
                AddExtension = true,
                Filter = "Rocket source (*.rocket)|*.rocket|All files (*.*)|*.*",
                CheckPathExists = true,
                OverwritePrompt = false,
            };

            if (saveDialog.ShowDialog(this) != true)
            {
                return;
            }

            path = saveDialog.FileName;
            if (File.Exists(path) || Directory.Exists(path))
            {
                MessageBox.Show(
                    this,
                    $"'{path}' already exists. Choose a new file name.",
                    "Create file",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            var dialog = new NameInputDialog("New File", "File name:", "untitled.rocket") { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                path = _workspaceFileSystem.ResolveChildPath(directory, dialog.Value);
            }
            catch (Exception exception) when (IsExpectedFileException(exception) || exception is ArgumentException)
            {
                ShowFileError("Create file failed", dialog.Value, exception);
                return;
            }
        }

        try
        {
            if (_viewModel.HasWorkspace)
            {
                SuppressWorkspaceChange(path);
            }

            await _workspaceFileSystem.CreateFileAsync(path, CancellationToken.None);
            if (_viewModel.HasWorkspace)
            {
                await _viewModel.Explorer.RefreshAsync(CancellationToken.None);
            }

            await OpenDocumentAsync(path);
        }
        catch (Exception exception) when (IsExpectedFileException(exception) || exception is ArgumentException)
        {
            ShowFileError("Create file failed", path, exception);
        }
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var directory = GetSelectedDirectory();
        if (directory is null)
        {
            return;
        }

        var dialog = new NameInputDialog("New Folder", "Folder name:", "NewFolder") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string path;
        try
        {
            path = _workspaceFileSystem.ResolveChildPath(directory, dialog.Value);
        }
        catch (Exception exception) when (IsExpectedFileException(exception) || exception is ArgumentException)
        {
            ShowFileError("Create folder failed", dialog.Value, exception);
            return;
        }

        try
        {
            SuppressWorkspaceChange(path);
            _workspaceFileSystem.CreateDirectory(path);
            await _viewModel.Explorer.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedFileException(exception) || exception is ArgumentException)
        {
            ShowFileError("Create folder failed", path, exception);
        }
    }

    private async void RenameExplorerItem_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedRealExplorerNode();
        if (node is null || IsWorkspaceRoot(node.Path))
        {
            return;
        }

        var dialog = new NameInputDialog("Rename", "New name:", node.Name) { Owner = this };
        if (dialog.ShowDialog() != true || string.Equals(dialog.Value, node.Name, StringComparison.Ordinal))
        {
            return;
        }

        var affected = GetDocumentsUnderPath(node.Path);
        if (!await PrepareDocumentsForFileOperationAsync(affected))
        {
            return;
        }

        try
        {
            var oldPath = node.Path;
            var newPath = _workspaceFileSystem.Rename(oldPath, dialog.Value);
            SuppressWorkspaceChange(oldPath);
            SuppressWorkspaceChange(newPath);
            CloseTabsWithoutPrompt(affected);
            await _viewModel.Explorer.RefreshAsync(CancellationToken.None);

            foreach (var tab in affected)
            {
                var mappedPath = MapRenamedPath(tab.Path, oldPath, newPath);
                if (File.Exists(mappedPath))
                {
                    await OpenDocumentAsync(mappedPath);
                }
            }
        }
        catch (Exception exception) when (IsExpectedFileException(exception) || exception is ArgumentException)
        {
            ShowFileError("Rename failed", node.Path, exception);
        }
    }

    private async void DeleteExplorerItem_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedRealExplorerNode();
        if (node is null || IsWorkspaceRoot(node.Path))
        {
            return;
        }

        var choice = MessageBox.Show(
            this,
            $"Move '{node.Name}' to the Recycle Bin?",
            "Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var affected = GetDocumentsUnderPath(node.Path);
        if (!await PrepareDocumentsForFileOperationAsync(affected))
        {
            return;
        }

        try
        {
            SuppressWorkspaceChange(node.Path);
            CloseTabsWithoutPrompt(affected);
            _workspaceFileSystem.DeleteToRecycleBin(node.Path);
            await _viewModel.Explorer.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedFileException(exception) || exception is ArgumentException)
        {
            ShowFileError("Delete failed", node.Path, exception);
        }
    }

    private void CopyExplorerPath_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedRealExplorerNode();
        if (node is not null)
        {
            Clipboard.SetText(node.Path);
        }
    }

    private void RevealExplorerItem_Click(object sender, RoutedEventArgs e)
    {
        var node = GetSelectedRealExplorerNode();
        if (node is null)
        {
            return;
        }

        var arguments = File.Exists(node.Path) ? $"/select,\"{node.Path}\"" : $"\"{node.Path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }

    private async void RefreshWorkspace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.Explorer.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedFileException(exception) || exception is ArgumentException)
        {
            ShowFileError("Refresh workspace failed", _viewModel.Explorer.Workspace?.Path ?? "workspace", exception);
        }
    }

    private void ExplorerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _selectedExplorerNode = e.NewValue as ExplorerNodeViewModel;

    private void ExplorerTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not TreeViewItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        if (current is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private async void ExplorerTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var node = GetSelectedRealExplorerNode();
        if (node is not null && !node.IsDirectory && File.Exists(node.Path))
        {
            await OpenDocumentAsync(node.Path);
            e.Handled = true;
        }
    }

    private async void ExplorerNode_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: ExplorerNodeViewModel node })
        {
            try
            {
                await node.LoadChildrenAsync(CancellationToken.None);
            }
            catch (Exception exception) when (IsExpectedFileException(exception))
            {
                ShowFileError("Folder enumeration failed", node.Path, exception);
            }
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveDocument is not null)
        {
            await SaveTabAsync(_viewModel.ActiveDocument);
        }
    }

    private async void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tab in _viewModel.Documents.ToArray())
        {
            if (tab.IsDirty && !await SaveTabAsync(tab))
            {
                break;
            }
        }
    }

    private async Task<bool> SaveTabAsync(DocumentTabViewModel tab)
    {
        try
        {
            SuppressWorkspaceChange(tab.Path);
            var result = await _documentStore.SaveAsync(tab.Id, overwriteExternalChanges: false, CancellationToken.None);
            if (result.Status == DocumentSaveStatus.Conflict)
            {
                var choice = MessageBox.Show(
                    this,
                    $"'{tab.DisplayName}' changed on disk after you opened or last saved it.\n\nOverwrite the external changes with the editor buffer?",
                    "File changed on disk",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (choice != MessageBoxResult.Yes)
                {
                    return false;
                }

                SuppressWorkspaceChange(tab.Path);
                result = await _documentStore.SaveAsync(tab.Id, overwriteExternalChanges: true, CancellationToken.None);
            }

            tab.UpdateSnapshot(result.Document);
            return !result.Document.IsDirty && result.Status is DocumentSaveStatus.Saved or DocumentSaveStatus.NoChanges;
        }
        catch (Exception exception) when (IsExpectedFileException(exception))
        {
            ShowFileError("Save failed", tab.Path, exception);
            return false;
        }
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveDocument is not null)
        {
            await TryCloseTabsAsync(new[] { _viewModel.ActiveDocument });
        }
    }

    private async void CloseOthers_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveDocument is null)
        {
            return;
        }

        var active = _viewModel.ActiveDocument;
        await TryCloseTabsAsync(_viewModel.Documents.Where(document => !ReferenceEquals(document, active)).ToArray());
    }

    private async void CloseAll_Click(object sender, RoutedEventArgs e) => await TryCloseTabsAsync(_viewModel.Documents.ToArray());

    private async void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DocumentTabViewModel tab })
        {
            await TryCloseTabsAsync(new[] { tab });
        }
    }

    private async Task<bool> TryCloseTabsAsync(IReadOnlyList<DocumentTabViewModel> tabs)
    {
        foreach (var tab in tabs)
        {
            if (!tab.IsDirty)
            {
                continue;
            }

            var choice = MessageBox.Show(
                this,
                $"Save changes to '{tab.DisplayName}'?",
                "Unsaved changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);

            if (choice == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (choice == MessageBoxResult.Yes && !await SaveTabAsync(tab))
            {
                return false;
            }
        }

        CloseTabsWithoutPrompt(tabs);
        return true;
    }

    private void CloseTabsWithoutPrompt(IReadOnlyList<DocumentTabViewModel> tabs)
    {
        foreach (var tab in tabs)
        {
            _documentStore.Close(tab.Id);
            _viewModel.Remove(tab);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.Redo();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.SelectAll();
    private void Find_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.ShowFind(includeReplace: false);
    private void Replace_Click(object sender, RoutedEventArgs e) => GetActiveEditor()?.ShowFind(includeReplace: true);
    private void GotoLine_Click(object sender, RoutedEventArgs e) => ShowGotoLine();

    private void ShowGotoLine()
    {
        var editor = GetActiveEditor();
        if (editor is null || _viewModel.ActiveDocument is null)
        {
            return;
        }

        var dialog = new GotoLineDialog(_viewModel.ActiveDocument.CaretLine, editor.EditorLineCount) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            editor.GoToLine(dialog.LineNumber);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(
            this,
            "RocketIDE\nNative Windows IDE for the Rocket programming language.\n\nDevelopment follows ROADMAP.md and compiler/LSP behavior remains owned by Rocket.",
            "About RocketIDE",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowWindowClose)
        {
            DisposeWorkspaceWatcher();
            return;
        }

        // WPF forbids calling Close() while the current Closing event is still being raised.
        // Rocket shutdown can complete synchronously (especially when the LSP is offline), so
        // always cancel this first close and schedule the final close on a later dispatcher turn.
        e.Cancel = true;
        if (_closePreparationInProgress)
        {
            return;
        }

        _closePreparationInProgress = true;
        try
        {
            if (_viewModel.Documents.Any(document => document.IsDirty) &&
                !await TryCloseTabsAsync(_viewModel.Documents.ToArray()))
            {
                return;
            }

            try
            {
                await ShutdownRocketIntegrationAsync(CancellationToken.None);
            }
            catch (Exception exception) when (IsExpectedRocketIntegrationException(exception))
            {
                AppendRocketOutput($"Rocket LSP shutdown failed: {exception.Message}");
            }

            // Force at least one dispatcher turn even when shutdown completed synchronously.
            // That guarantees the original Closing event has returned before Close() is called.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            _allowWindowClose = true;
            DisposeWorkspaceWatcher();
            Close();
        }
        finally
        {
            if (!_allowWindowClose)
            {
                _closePreparationInProgress = false;
            }
        }
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!control)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.N:
                e.Handled = true;
                NewFile_Click(sender, e);
                break;
            case Key.O when shift:
                e.Handled = true;
                OpenFolder_Click(sender, e);
                break;
            case Key.O:
                e.Handled = true;
                await OpenFilesAsync();
                break;
            case Key.S when shift:
                e.Handled = true;
                foreach (var tab in _viewModel.Documents.ToArray())
                {
                    if (tab.IsDirty && !await SaveTabAsync(tab))
                    {
                        break;
                    }
                }
                break;
            case Key.S:
                e.Handled = true;
                if (_viewModel.ActiveDocument is not null)
                {
                    await SaveTabAsync(_viewModel.ActiveDocument);
                }
                break;
            case Key.W when shift:
                e.Handled = true;
                await TryCloseTabsAsync(_viewModel.Documents.ToArray());
                break;
            case Key.W:
                e.Handled = true;
                if (_viewModel.ActiveDocument is not null)
                {
                    await TryCloseTabsAsync(new[] { _viewModel.ActiveDocument });
                }
                break;
            case Key.F:
                e.Handled = true;
                GetActiveEditor()?.ShowFind(includeReplace: false);
                break;
            case Key.H:
                e.Handled = true;
                GetActiveEditor()?.ShowFind(includeReplace: true);
                break;
            case Key.G:
                e.Handled = true;
                ShowGotoLine();
                break;
        }
    }

    private EditorDocumentHost? GetActiveEditor()
    {
        var active = _viewModel.ActiveDocument;
        return active is null ? null : FindEditorForDataContext(EditorTabs, active);
    }

    private static EditorDocumentHost? FindEditorForDataContext(DependencyObject parent, DocumentTabViewModel active)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is EditorDocumentHost editor && ReferenceEquals(editor.DataContext, active))
            {
                return editor;
            }

            var nested = FindEditorForDataContext(child, active);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void WorkspaceWatcher_ChangesAvailable(object? sender, WorkspaceChangesEventArgs e)
    {
        // A notification can already be in flight when a workspace switch disposes the old
        // watcher. Never apply stale structural/file events to the newly active workspace.
        if (!ReferenceEquals(sender, _workspaceWatcher) || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                // The helper catches/logs its own failures, so no faulted nested task is abandoned.
                _ = HandleWorkspaceChangesSafelyAsync(e.Changes);
            });
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
        }
    }

    private async Task HandleWorkspaceChangesSafelyAsync(IReadOnlyList<WorkspaceChange> changes)
    {
        try
        {
            await HandleWorkspaceChangesAsync(changes);
        }
        catch (Exception exception)
        {
            Trace.TraceError($"RocketIDE workspace watcher update failed: {exception}");
        }
    }

    private async Task HandleWorkspaceChangesAsync(IReadOnlyList<WorkspaceChange> changes)
    {
        var structuralChange = false;
        foreach (var change in changes)
        {
            if (IsSuppressedWorkspaceChange(change.Path) || (change.OldPath is not null && IsSuppressedWorkspaceChange(change.OldPath)))
            {
                continue;
            }

            structuralChange |= change.Kind is WorkspaceChangeKind.Created or WorkspaceChangeKind.Deleted or WorkspaceChangeKind.Renamed;
            switch (change.Kind)
            {
                case WorkspaceChangeKind.Changed:
                    await HandleExternallyChangedFileAsync(change.Path);
                    break;
                case WorkspaceChangeKind.Deleted:
                    HandleExternallyDeletedFile(change.Path);
                    break;
                case WorkspaceChangeKind.Renamed when change.OldPath is not null:
                    await HandleExternallyRenamedFileAsync(change.OldPath, change.Path);
                    break;
            }
        }

        if (structuralChange && _viewModel.HasWorkspace)
        {
            await _viewModel.Explorer.RefreshAsync(CancellationToken.None);
        }

        if (changes.Any(IsRocketManifestChange))
        {
            UpdateActiveTargetStatus();
        }
    }

    private async Task HandleExternallyChangedFileAsync(string path)
    {
        var tab = FindOpenDocument(path);
        if (tab is null || tab.IsDirty || !File.Exists(path))
        {
            return;
        }

        var choice = MessageBox.Show(
            this,
            $"'{tab.DisplayName}' changed outside RocketIDE. Reload it from disk?",
            "File changed on disk",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var snapshot = await _documentStore.ReloadAsync(tab.Id, CancellationToken.None);
            tab.UpdateSnapshot(snapshot);
        }
        catch (Exception exception) when (IsExpectedFileException(exception) || exception is InvalidOperationException)
        {
            ShowFileError("Reload failed", tab.Path, exception);
        }
    }

    private void HandleExternallyDeletedFile(string path)
    {
        var tab = FindOpenDocument(path);
        if (tab is null)
        {
            return;
        }

        MessageBox.Show(
            this,
            $"'{tab.DisplayName}' was deleted outside RocketIDE. Its editor buffer has been kept open and will not be overwritten silently.",
            "File deleted on disk",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async Task HandleExternallyRenamedFileAsync(string oldPath, string newPath)
    {
        var tab = FindOpenDocument(oldPath);
        if (tab is null)
        {
            return;
        }

        if (tab.IsDirty)
        {
            MessageBox.Show(
                this,
                $"'{tab.DisplayName}' was renamed outside RocketIDE while this buffer has unsaved changes. The buffer was kept on its original path so no edits are lost.",
                "File renamed on disk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var choice = MessageBox.Show(
            this,
            $"'{tab.DisplayName}' was renamed to '{Path.GetFileName(newPath)}'. Follow the rename?",
            "File renamed on disk",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);
        if (choice == MessageBoxResult.Yes && File.Exists(newPath))
        {
            CloseTabsWithoutPrompt(new[] { tab });
            await OpenDocumentAsync(newPath);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ActiveDocument))
        {
            UpdateActiveTargetStatus();
        }
    }

    private static bool IsRocketManifestChange(WorkspaceChange change) =>
        string.Equals(Path.GetFileName(change.Path), "rocket.toml", StringComparison.OrdinalIgnoreCase) ||
        (change.OldPath is not null &&
         string.Equals(Path.GetFileName(change.OldPath), "rocket.toml", StringComparison.OrdinalIgnoreCase));

    private void UpdateActiveTargetStatus()
    {
        var activePath = _viewModel.ActiveDocument?.Path;
        var target = activePath is null ? null : _targetDiscovery.Discover(activePath);
        _viewModel.ActiveTargetStatus = target switch
        {
            null => "Target: none",
            { IsStandalone: true } => $"Target: {Path.GetFileName(target.InputPath)} (standalone)",
            _ => $"Target: {Path.GetFileName(target.WorkingDirectory)}",
        };
    }

    private string? GetSelectedDirectory()
    {
        if (!_viewModel.HasWorkspace || _viewModel.Explorer.Workspace is null)
        {
            return null;
        }

        var node = GetSelectedRealExplorerNode();
        if (node is null)
        {
            return _viewModel.Explorer.Workspace.Path;
        }

        return node.IsDirectory ? node.Path : Path.GetDirectoryName(node.Path);
    }

    private ExplorerNodeViewModel? GetSelectedRealExplorerNode() =>
        _selectedExplorerNode is { IsPlaceholder: false } node ? node : null;

    private bool IsWorkspaceRoot(string path) =>
        _viewModel.Explorer.Workspace is { } workspace &&
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(workspace.Path), StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<DocumentTabViewModel> GetDocumentsUnderPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            return _viewModel.Documents.Where(tab => string.Equals(tab.Path, fullPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        var prefix = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return _viewModel.Documents.Where(tab => tab.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private async Task<bool> PrepareDocumentsForFileOperationAsync(IReadOnlyList<DocumentTabViewModel> tabs)
    {
        foreach (var tab in tabs)
        {
            if (tab.IsDirty && !await SaveTabAsync(tab))
            {
                return false;
            }
        }

        return true;
    }

    private DocumentTabViewModel? FindOpenDocument(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return _viewModel.Documents.FirstOrDefault(tab => string.Equals(tab.Path, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string MapRenamedPath(string documentPath, string oldPath, string newPath)
    {
        if (string.Equals(documentPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            return newPath;
        }

        var relative = Path.GetRelativePath(oldPath, documentPath);
        return Path.GetFullPath(Path.Combine(newPath, relative));
    }

    private void SuppressWorkspaceChange(string path)
    {
        _suppressedWorkspaceChanges[Path.GetFullPath(path)] = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    }

    private bool IsSuppressedWorkspaceChange(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!_suppressedWorkspaceChanges.TryGetValue(fullPath, out var until))
        {
            return false;
        }

        if (until >= DateTime.UtcNow)
        {
            return true;
        }

        _suppressedWorkspaceChanges.Remove(fullPath);
        return false;
    }

    private void DisposeWorkspaceWatcher()
    {
        var watcher = _workspaceWatcher;
        _workspaceWatcher = null;
        DisposeWorkspaceWatcher(watcher);
    }

    private void DisposeWorkspaceWatcher(WorkspaceFileWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        watcher.ChangesAvailable -= WorkspaceWatcher_ChangesAvailable;
        watcher.Dispose();
    }

    private static bool IsExpectedFileException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private void ShowFileError(string title, string path, Exception exception) =>
        MessageBox.Show(
            this,
            $"{title}:\n{path}\n\n{exception.Message}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
