using System.Windows;

namespace RocketIDE.App.Editor;

public partial class GotoLineDialog : Window
{
    private readonly int _maximumLine;

    public GotoLineDialog(int currentLine, int maximumLine)
    {
        InitializeComponent();
        _maximumLine = Math.Max(1, maximumLine);
        PromptText.Text = $"Line number (1–{_maximumLine})";
        LineTextBox.Text = Math.Clamp(currentLine, 1, _maximumLine).ToString();
        LineTextBox.SelectAll();
        Loaded += (_, _) => LineTextBox.Focus();
    }

    public int LineNumber { get; private set; } = 1;

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LineTextBox.Text, out var line) || line < 1 || line > _maximumLine)
        {
            MessageBox.Show(this, $"Enter a line from 1 to {_maximumLine}.", "Go to Line", MessageBoxButton.OK, MessageBoxImage.Information);
            LineTextBox.Focus();
            LineTextBox.SelectAll();
            return;
        }

        LineNumber = line;
        DialogResult = true;
    }
}
