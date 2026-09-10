using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RocketIDE.App.Editor;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Documents;
using RocketIDE.Infrastructure.Files;

namespace RocketIDE.App;

public partial class MainWindow : Window
{
    private readonly FileDocumentStore _documentStore = new();
    private readonly MainWindowViewModel _viewModel = new();
    private bool _allowWindowClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

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

                result = await _documentStore.SaveAsync(tab.Id, overwriteExternalChanges: true, CancellationToken.None);
            }

            tab.UpdateSnapshot(result.Document);
            return !result.Document.IsDirty &&
                (result.Status is DocumentSaveStatus.Saved or DocumentSaveStatus.NoChanges);
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

    private async void CloseAll_Click(object sender, RoutedEventArgs e) =>
        await TryCloseTabsAsync(_viewModel.Documents.ToArray());

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

        foreach (var tab in tabs)
        {
            _documentStore.Close(tab.Id);
            _viewModel.Remove(tab);
        }

        return true;
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

        var dialog = new GotoLineDialog(_viewModel.ActiveDocument.CaretLine, editor.EditorLineCount)
        {
            Owner = this,
        };

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
        if (_allowWindowClose || !_viewModel.Documents.Any(document => document.IsDirty))
        {
            return;
        }

        e.Cancel = true;
        if (await TryCloseTabsAsync(_viewModel.Documents.ToArray()))
        {
            _allowWindowClose = true;
            Close();
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
