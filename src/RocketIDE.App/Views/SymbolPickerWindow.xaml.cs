using System.Windows;
using System.Windows.Input;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Views;

public partial class SymbolPickerWindow : Window
{
    public SymbolPickerWindow(string title, SymbolSearchViewModel viewModel)
    {
        InitializeComponent();
        Title = title;
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Loaded += (_, _) => QueryBox.Focus();
    }

    public SymbolSearchItem? SelectedSymbol { get; private set; }

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
        if (e.Key == Key.Down && SymbolList.Items.Count > 0)
        {
            SymbolList.SelectedIndex = Math.Min(SymbolList.Items.Count - 1, SymbolList.SelectedIndex + 1);
            SymbolList.ScrollIntoView(SymbolList.SelectedItem);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Up && SymbolList.Items.Count > 0)
        {
            SymbolList.SelectedIndex = Math.Max(0, SymbolList.SelectedIndex - 1);
            SymbolList.ScrollIntoView(SymbolList.SelectedItem);
            e.Handled = true;
        }
    }

    private void SymbolList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
        if (DataContext is not SymbolSearchViewModel { SelectedItem: { } item }) return;
        SelectedSymbol = item;
        DialogResult = true;
    }
}
