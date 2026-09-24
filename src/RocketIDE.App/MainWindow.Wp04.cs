using System.IO;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Editor;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Infrastructure.Settings;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App;

public partial class MainWindow
{
    private readonly EditorPreferenceService _editorPreferenceService = new();
    private readonly EditorPreferencesStore _editorPreferencesStore = EditorPreferencesStore.CreateDefault();
    private readonly ClosedEditorHistory _closedEditorHistory = new(capacity: 20);
    private readonly RecentEditorHistory _recentEditorHistory = new(capacity: 64);
    private readonly SemaphoreSlim _editorPreferenceSaveGate = new(1, 1);
    private FormatOnSaveService _formatOnSaveService = null!;

    private void InitializeWp04()
    {
        _formatOnSaveService = new FormatOnSaveService(AppendRocketOutput);
        EditorContext.ActiveContextChanged += EditorIntegration_ActiveContextChanged;
        _editorPreferenceService.Changed += EditorPreferenceService_Changed;
        Loaded += Wp04_Loaded;
        Closed += Wp04_Closed;
    }

    private async void Wp04_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var preferences = await _editorPreferencesStore.LoadAsync(_lifetime.WorkToken);
            _editorPreferenceService.Apply(preferences);
            UpdateWp04PreferenceMenuState();
            ApplyEditorPreferencesToActiveView();
        }
        catch (OperationCanceledException) when (_lifetime.IsStopping)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppendRocketOutput($"Editor preferences could not be loaded: {exception.Message}");
        }
    }

    private void Wp04_Closed(object? sender, EventArgs e)
    {
        EditorContext.ActiveContextChanged -= EditorIntegration_ActiveContextChanged;
        _editorPreferenceService.Changed -= EditorPreferenceService_Changed;
        Loaded -= Wp04_Loaded;
        Closed -= Wp04_Closed;
    }

    private void EditorIntegration_ActiveContextChanged(object? sender, EventArgs e)
    {
        if (EditorContext.ActiveDocument is { } active)
        {
            _recentEditorHistory.Record(active.Path);
        }
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }
        _ = Dispatcher.BeginInvoke(new Action(ApplyEditorPreferencesToActiveView), DispatcherPriority.Loaded);
    }

    private void EditorPreferenceService_Changed(object? sender, EventArgs e)
    {
        UpdateWp04PreferenceMenuState();
        ApplyEditorPreferencesToActiveView();
    }

    private void ApplyEditorPreferencesToActiveView()
    {
        if (EditorContext.ActiveView?.CommandTarget is IEditorPresentationTarget presentation)
        {
            presentation.ApplyEditorPreferences(_editorPreferenceService.Current);
        }
    }

    private void UpdateWp04PreferenceMenuState()
    {
        if (WordWrapViewMenu is not null)
        {
            WordWrapViewMenu.IsChecked = _editorPreferenceService.Current.WordWrap;
        }
        if (FormatOnSaveViewMenu is not null)
        {
            FormatOnSaveViewMenu.IsChecked = _editorPreferenceService.Current.FormatOnSave;
        }
    }

    private async Task PersistEditorPreferencesAsync()
    {
        try
        {
            await _editorPreferenceSaveGate.WaitAsync(_lifetime.WorkToken);
            try
            {
                await _editorPreferencesStore.SaveAsync(_editorPreferenceService.Current, _lifetime.WorkToken);
            }
            finally
            {
                _editorPreferenceSaveGate.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsStopping)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppendRocketOutput($"Editor preferences could not be saved: {exception.Message}");
        }
    }

    private bool TryGetActiveEditableEditor(out TextDocument document, out IEditorCommandTarget commandTarget)
    {
        var view = EditorContext.ActiveView;
        if (view is null || view.CommandTarget is null || !view.Document.AllowLocalEditing)
        {
            document = null!;
            commandTarget = null!;
            return false;
        }

        document = view.Document.EditorDocument;
        commandTarget = view.CommandTarget;
        return true;
    }

    private void ToggleLineComment_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveEditableEditor(out var document, out var target))
        {
            _ = EditorTextOperations.ToggleLineComment(document, target);
        }
    }

    private void DuplicateLineOrSelection_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveEditableEditor(out var document, out var target))
        {
            _ = EditorTextOperations.DuplicateLineOrSelection(document, target);
        }
    }

    private void MoveLineOrSelectionUp_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveEditableEditor(out var document, out var target))
        {
            _ = EditorTextOperations.MoveLineOrSelectionUp(document, target);
        }
    }

    private void MoveLineOrSelectionDown_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveEditableEditor(out var document, out var target))
        {
            _ = EditorTextOperations.MoveLineOrSelectionDown(document, target);
        }
    }

    private void DeleteLineOrSelection_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActiveEditableEditor(out var document, out var target))
        {
            _ = EditorTextOperations.DeleteLineOrSelection(document, target);
        }
    }

    private async void ReopenClosedEditor_Click(object sender, RoutedEventArgs e)
    {
        while (_closedEditorHistory.TryPop(out var path))
        {
            if (!File.Exists(path))
            {
                AppendRocketOutput($"Skipped closed editor because the file no longer exists: {path}");
                continue;
            }

            try
            {
                if (await EditorNavigation.OpenOrRevealAsync(path, cancellationToken: _lifetime.WorkToken) is not null)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsStopping)
            {
                return;
            }
            catch (Exception exception) when (IsExpectedFileException(exception))
            {
                AppendRocketOutput($"Could not reopen closed editor '{path}': {exception.Message}");
            }
        }
    }

    private void RecordClosedEditors(IEnumerable<DocumentTabViewModel> documents)
    {
        foreach (var document in documents)
        {
            _closedEditorHistory.Record(document.Path);
        }
    }

    private async void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _editorPreferenceService.ZoomIn();
        await PersistEditorPreferencesAsync();
    }

    private async void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _editorPreferenceService.ZoomOut();
        await PersistEditorPreferencesAsync();
    }

    private async void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        _editorPreferenceService.ResetZoom();
        await PersistEditorPreferencesAsync();
    }

    private async void WordWrap_Click(object sender, RoutedEventArgs e)
    {
        _editorPreferenceService.SetWordWrap(WordWrapViewMenu.IsChecked);
        await PersistEditorPreferencesAsync();
    }

    private async void FormatOnSave_Click(object sender, RoutedEventArgs e)
    {
        _editorPreferenceService.SetFormatOnSave(FormatOnSaveViewMenu.IsChecked);
        await PersistEditorPreferencesAsync();
    }

    private void CollapseAllExplorer_Click(object sender, RoutedEventArgs e) =>
        _viewModel.Explorer.CollapseAll();

    private async void RevealActiveFile_Click(object sender, RoutedEventArgs e)
    {
        var active = EditorContext.ActiveDocument;
        if (active is null)
        {
            return;
        }

        try
        {
            _ = await _viewModel.Explorer.RevealAsync(active.Path, _lifetime.WorkToken);
        }
        catch (OperationCanceledException) when (_lifetime.IsStopping)
        {
        }
        catch (Exception exception) when (IsExpectedFileException(exception) || exception is ArgumentException)
        {
            AppendRocketOutput($"Reveal Active File failed: {exception.Message}");
        }
    }

    private QuickOpenDialog CreateWp04QuickOpenDialog(string workspacePath)
    {
        var finder = new QuickOpenFileFinder(_workspaceFileSystem);
        var search = new QuickOpenSearchService(finder);
        var openPaths = _viewModel.Documents.Select(document => document.Path).ToArray();
        return new QuickOpenDialog(workspacePath, search, openPaths, _recentEditorHistory.Snapshot)
        {
            Owner = this,
        };
    }

    private async Task<RocketSessionDocument> PrepareDocumentForSaveAsync(
        DocumentTabViewModel tab,
        RocketSessionDocument synchronizedDocument,
        CancellationToken cancellationToken)
    {
        if (!_editorPreferenceService.Current.FormatOnSave || !IsRocketPath(tab.Path) || !tab.AllowLsp)
        {
            return GetSessionDocumentOnUi(tab);
        }

        return await _formatOnSaveService.PrepareAsync(
            synchronizedDocument,
            () => GetSessionDocumentOnUi(tab),
            token => _rocketSession.RequestFormattingAsync(tab.Path, 4, true, token),
            (edits, token) => ApplyFormattingEditsOnUiAsync(tab, synchronizedDocument.Version, edits, token),
            cancellationToken);
    }

    private RocketSessionDocument GetSessionDocumentOnUi(DocumentTabViewModel tab)
    {
        if (Dispatcher.CheckAccess())
        {
            return ToSessionDocument(tab);
        }
        return Dispatcher.Invoke(() => ToSessionDocument(tab));
    }

    private Task ApplyFormattingEditsOnUiAsync(
        DocumentTabViewModel tab,
        int expectedVersion,
        IReadOnlyList<RocketTextEdit> edits,
        CancellationToken cancellationToken)
    {
        if (Dispatcher.CheckAccess())
        {
            return ApplyFormattingEditsAsync(tab, expectedVersion, edits, cancellationToken);
        }

        return Dispatcher.InvokeAsync(
            () => ApplyFormattingEditsAsync(tab, expectedVersion, edits, cancellationToken),
            DispatcherPriority.Send,
            cancellationToken).Task.Unwrap();
    }

    private async Task ApplyFormattingEditsAsync(
        DocumentTabViewModel tab,
        int expectedVersion,
        IReadOnlyList<RocketTextEdit> edits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var edit = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(tab.Path, expectedVersion, edits)]);
        _ = await ApplyWorkspaceEditAsync(edit, tab.Path, cancellationToken);
    }
}
