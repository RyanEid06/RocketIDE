using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RocketIDE.App.Editor;
using RocketIDE.App.Editor.Snippets;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App;

public partial class MainWindow
{
    private readonly RocketSnippetService _snippetService = new();

    private void InitializeSplitEditing()
    {
        _viewModel.EditorLayout.LayoutChanged += (_, _) => RefreshEditorGroupLayout();
        _viewModel.EditorLayout.ActiveContextChanged += (_, _) =>
        {
            RefreshEditorGroupLayout();
            ScheduleActiveEditorFocus();
        };
        RefreshEditorGroupLayout();
    }

    private void RefreshEditorGroupLayout()
    {
        var layout = _viewModel.EditorLayout;
        PrimaryEditorGroupHost.DataContext = layout.Groups[0];
        var hasSecond = layout.Groups.Count == 2;
        SecondaryEditorGroupHost.DataContext = hasSecond ? layout.Groups[1] : null;
        SecondaryEditorGroupHost.Visibility = hasSecond ? Visibility.Visible : Visibility.Collapsed;
        EditorGroupSplitter.Visibility = hasSecond ? Visibility.Visible : Visibility.Collapsed;

        EditorGroupColumn0.Width = new GridLength(1, GridUnitType.Star);
        EditorGroupColumn1.Width = new GridLength(0);
        EditorGroupColumn2.Width = new GridLength(0);
        EditorGroupRow0.Height = new GridLength(1, GridUnitType.Star);
        EditorGroupRow1.Height = new GridLength(0);
        EditorGroupRow2.Height = new GridLength(0);

        Grid.SetRow(PrimaryEditorGroupHost, 0);
        Grid.SetColumn(PrimaryEditorGroupHost, 0);
        Grid.SetRowSpan(PrimaryEditorGroupHost, 1);
        Grid.SetColumnSpan(PrimaryEditorGroupHost, 1);
        Grid.SetRow(SecondaryEditorGroupHost, 0);
        Grid.SetColumn(SecondaryEditorGroupHost, 0);
        Grid.SetRow(EditorGroupSplitter, 0);
        Grid.SetColumn(EditorGroupSplitter, 0);

        if (!hasSecond) return;

        if (layout.Orientation == EditorSplitOrientation.Horizontal)
        {
            EditorGroupRow1.Height = new GridLength(5);
            EditorGroupRow2.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(SecondaryEditorGroupHost, 2);
            Grid.SetColumn(SecondaryEditorGroupHost, 0);
            Grid.SetRow(EditorGroupSplitter, 1);
            Grid.SetColumn(EditorGroupSplitter, 0);
            EditorGroupSplitter.ResizeDirection = GridResizeDirection.Rows;
            EditorGroupSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            EditorGroupSplitter.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            EditorGroupColumn1.Width = new GridLength(5);
            EditorGroupColumn2.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(SecondaryEditorGroupHost, 0);
            Grid.SetColumn(SecondaryEditorGroupHost, 2);
            Grid.SetRow(EditorGroupSplitter, 0);
            Grid.SetColumn(EditorGroupSplitter, 1);
            EditorGroupSplitter.ResizeDirection = GridResizeDirection.Columns;
            EditorGroupSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            EditorGroupSplitter.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    private void ScheduleActiveEditorFocus()
    {
        var view = _viewModel.ActiveView;
        if (view is null) return;
        _ = Dispatcher.BeginInvoke(new Action(view.Focus), DispatcherPriority.Input);
    }

    private void EditorGroup_ViewActivated(object? sender, EditorViewEventArgs e) =>
        _viewModel.EditorLayout.ActivateView(e.View);

    private async void EditorGroup_ViewCloseRequested(object? sender, EditorViewEventArgs e) =>
        await TryCloseViewsAsync([e.View]);

    private async Task<bool> TryCloseViewsAsync(IReadOnlyList<EditorViewViewModel> requested)
    {
        if (_lifetime.IsStopping) return false;
        var views = requested.Distinct().ToArray();
        if (views.Length == 0) return true;

        var documentsToClose = EditorClosePlanner.LogicalDocumentsClosing(_viewModel.EditorLayout, views);

        if (!await ConfirmDirtyTabsAsync(documentsToClose)) return false;

        foreach (var view in views)
            _viewModel.EditorLayout.CloseView(view);

        foreach (var document in documentsToClose)
        {
            if (_viewModel.EditorLayout.CountViews(document) == 0)
            {
                _documentStore.Close(document.Id);
                _viewModel.Remove(document);
            }
        }

        await SaveRecoverySnapshotAsync();
        return true;
    }

    private void SplitRight_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveView is null) return;
        _viewModel.EditorLayout.SplitActive(EditorSplitOrientation.Vertical);
        ScheduleActiveEditorFocus();
    }

    private void SplitDown_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveView is null) return;
        _viewModel.EditorLayout.SplitActive(EditorSplitOrientation.Horizontal);
        ScheduleActiveEditorFocus();
    }

    private async void CloseGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.EditorLayout.Groups.Count < 2) return;
        var views = _viewModel.EditorLayout.ActiveGroup.Views.ToArray();
        await TryCloseViewsAsync(views);
    }

    private void FocusNextGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.EditorLayout.FocusNextGroup())
            ScheduleActiveEditorFocus();
    }

    private void MoveActiveEditorToOtherGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.EditorLayout.MoveActiveToOtherGroup())
            ScheduleActiveEditorFocus();
    }

    private void InsertSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string trigger } || _viewModel.ActiveView is not { } view) return;
        var snippet = RocketSnippetCatalog.Find(trigger);
        if (snippet is null) return;
        try
        {
            _snippetService.Insert(view, snippet);
        }
        catch (InvalidOperationException exception)
        {
            _viewModel.AppendOutput($"Snippet insertion unavailable: {exception.Message}");
        }
    }

    private bool TryHandleSnippetNavigation(KeyEventArgs e)
    {
        var view = _viewModel.ActiveView;
        var session = view?.SnippetSession;
        if (session is null || !session.IsActive) return false;

        if (e.Key == Key.Escape)
        {
            session.Cancel();
            if (view is not null) view.SnippetSession = null;
            return true;
        }

        var modifiers = Keyboard.Modifiers;
        if (e.Key != Key.Tab || !RocketSnippetNavigation.ShouldHandleTab(modifiers))
        {
            return false;
        }

        var handled = modifiers == ModifierKeys.Shift
            ? session.MovePrevious()
            : session.MoveNext();

        if (!session.IsActive && view is not null)
        {
            view.SnippetSession = null;
        }

        return handled;
    }
}
