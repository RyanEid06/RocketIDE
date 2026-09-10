using System.Windows;
using RocketIDE.App.Interop;
using System.Windows.Input;

namespace RocketIDE.App.Editor;

public partial class NameInputDialog : Window
{
    public NameInputDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowsTitleBar.ApplyDarkMode(this);
        Title = title;
        PromptText.Text = prompt;
        ValueTextBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public string Value => ValueTextBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueTextBox.Text))
        {
            MessageBox.Show(this, "A name is required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void ValueTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
            e.Handled = true;
        }
    }
}
