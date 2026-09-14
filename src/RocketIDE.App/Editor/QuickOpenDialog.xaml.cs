using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RocketIDE.App.Editor;

public partial class QuickOpenDialog : Window
{
    private readonly string _rootPath;
    private readonly IReadOnlyList<QuickOpenItem> _items;

    public QuickOpenDialog(string rootPath, IReadOnlyList<string> files)
    {
        InitializeComponent();
        _rootPath = Path.GetFullPath(rootPath);
        _items = files
            .Select(path => new QuickOpenItem(Path.GetFullPath(path), Path.GetRelativePath(_rootPath, path)))
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        RefreshItems();
    }

    public string? SelectedPath { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshItems();

    private void RefreshItems()
    {
        if (FilesList is null || FilterBox is null)
        {
            return;
        }

        var filter = FilterBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _items
            : _items.Where(item =>
                    item.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    item.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        FilesList.ItemsSource = filtered.Take(250).ToArray();
        if (FilesList.Items.Count > 0)
        {
            FilesList.SelectedIndex = 0;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && FilterBox.IsKeyboardFocusWithin && FilesList.Items.Count > 0)
        {
            FilesList.Focus();
            FilesList.SelectedIndex = Math.Max(0, FilesList.SelectedIndex);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            AcceptSelection();
            e.Handled = true;
        }
    }

    private void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();

    private void Open_Click(object sender, RoutedEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
        if (FilesList.SelectedItem is not QuickOpenItem item)
        {
            return;
        }

        SelectedPath = item.FullPath;
        DialogResult = true;
    }

    private sealed record QuickOpenItem(string FullPath, string RelativePath)
    {
        public string FileName => Path.GetFileName(FullPath);
    }
}
