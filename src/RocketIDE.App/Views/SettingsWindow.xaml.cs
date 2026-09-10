using System.Windows;
using Microsoft.Win32;
using RocketIDE.App.Interop;
using RocketIDE.Infrastructure.Settings;

namespace RocketIDE.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(RocketToolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        SourceInitialized += (_, _) => WindowsTitleBar.ApplyDarkMode(this);
        CompilerPathTextBox.Text = settings.CompilerPath ?? string.Empty;
        LanguageServerPathTextBox.Text = settings.LanguageServerPath ?? string.Empty;
    }

    public RocketToolSettings Settings => new(
        NullIfWhiteSpace(CompilerPathTextBox.Text),
        NullIfWhiteSpace(LanguageServerPathTextBox.Text));

    private void BrowseCompiler_Click(object sender, RoutedEventArgs e) => BrowseInto(CompilerPathTextBox, "rocketc.exe");
    private void BrowseLanguageServer_Click(object sender, RoutedEventArgs e) => BrowseInto(LanguageServerPathTextBox, "rocket-lsp.exe");

    private void Automatic_Click(object sender, RoutedEventArgs e)
    {
        CompilerPathTextBox.Clear();
        LanguageServerPathTextBox.Clear();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void BrowseInto(System.Windows.Controls.TextBox textBox, string expectedFileName)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Select {expectedFileName}",
            Filter = $"{expectedFileName}|{expectedFileName}|Executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            textBox.Text = dialog.FileName;
        }
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
