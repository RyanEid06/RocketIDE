using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Views;

public partial class OutlineWindow : Window
{
    private readonly OutlineViewModel _viewModel;

    public OutlineWindow(OutlineViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
    }

    private async void OutlineTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(OutlineTree, source) is TreeViewItem { DataContext: OutlineNodeViewModel node })
        {
            await _viewModel.NavigateAsync(node);
        }
    }

    private async void OutlineTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OutlineTree.SelectedItem is OutlineNodeViewModel node)
        {
            e.Handled = true;
            await _viewModel.NavigateAsync(node);
        }
    }
}
