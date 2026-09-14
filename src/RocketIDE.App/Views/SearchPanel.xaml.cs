using System.Windows;
using System.Windows.Controls;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Views;

public partial class SearchPanel : UserControl
{
    public SearchPanel()
    {
        InitializeComponent();
    }

    public void FocusPattern(bool includeReplace)
    {
        ResultsTabs.SelectedIndex = includeReplace ? 1 : 0;
        if (includeReplace)
        {
            ReplacementBox.Focus();
            return;
        }

        PatternBox.Focus();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SearchViewModel viewModel)
        {
            ResultsTabs.SelectedIndex = 0;
            await viewModel.SearchAsync(CancellationToken.None);
        }
    }

    private async void PreviewReplace_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SearchViewModel viewModel)
        {
            ResultsTabs.SelectedIndex = 1;
            await viewModel.PreviewReplaceAsync(CancellationToken.None);
        }
    }

    private async void ApplyReplace_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SearchViewModel viewModel)
        {
            ResultsTabs.SelectedIndex = 1;
            await viewModel.ApplyReplaceAsync(CancellationToken.None);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SearchViewModel viewModel)
        {
            viewModel.CancelCurrentOperation();
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is SearchViewModel viewModel && ResultsList.SelectedItem is SearchResultViewModel result)
        {
            viewModel.Activate(result);
        }
    }
}
