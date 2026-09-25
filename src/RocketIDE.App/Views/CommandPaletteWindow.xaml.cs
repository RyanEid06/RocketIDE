using System.Windows;
using System.Windows.Input;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Views;

public partial class CommandPaletteWindow : Window
{
    public CommandPaletteWindow(CommandPaletteViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Loaded += (_, _) =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        };
    }

    public string? SelectedCommandId { get; private set; }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            AcceptSelection();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Down && CommandList.Items.Count > 0)
        {
            CommandList.SelectedIndex = Math.Min(CommandList.Items.Count - 1, CommandList.SelectedIndex + 1);
            CommandList.ScrollIntoView(CommandList.SelectedItem);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Up && CommandList.Items.Count > 0)
        {
            CommandList.SelectedIndex = Math.Max(0, CommandList.SelectedIndex - 1);
            CommandList.ScrollIntoView(CommandList.SelectedItem);
            e.Handled = true;
        }
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
        if (DataContext is not CommandPaletteViewModel { SelectedItem: { IsEnabled: true } item }) return;
        SelectedCommandId = item.Definition.Id;
        DialogResult = true;
    }
}
