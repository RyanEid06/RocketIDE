using System.IO;
using System.Windows;
using Microsoft.Win32;
using RocketIDE.App.Interop;
using RocketIDE.Infrastructure.Settings;

namespace RocketIDE.App.Views;

public partial class SettingsWindow : Window
{
    private readonly HashSet<string> _trustedRoots;
    private readonly string? _currentWorkspacePath;
    private bool _resetTrust;

    public SettingsWindow(RocketToolSettings settings, string? currentWorkspacePath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        SourceInitialized += (_, _) => WindowsTitleBar.ApplyDarkMode(this);
        CompilerPathTextBox.Text = settings.CompilerPath ?? string.Empty;
        LanguageServerPathTextBox.Text = settings.LanguageServerPath ?? string.Empty;
        _trustedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in settings.TrustedCheckoutRoots ?? [])
        {
            if (TryNormalizeDirectory(root, requireExisting: false, out var normalized))
            {
                _trustedRoots.Add(normalized);
            }
        }
        _currentWorkspacePath = TryNormalizeDirectory(currentWorkspacePath, requireExisting: true, out var workspace) && !IsFileSystemRoot(workspace)
            ? workspace
            : null;

        if (_currentWorkspacePath is null)
        {
            TrustWorkspaceCheckBox.IsEnabled = false;
            TrustWorkspaceCheckBox.IsChecked = false;
            WorkspaceTrustPathText.Text = "Open a non-drive-root workspace before trusting checkout-local build outputs.";
        }
        else
        {
            TrustWorkspaceCheckBox.IsChecked = _trustedRoots.Contains(_currentWorkspacePath);
            WorkspaceTrustPathText.Text = _currentWorkspacePath;
        }
    }

    public RocketToolSettings Settings
    {
        get
        {
            var roots = _resetTrust
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(_trustedRoots, StringComparer.OrdinalIgnoreCase);

            if (_currentWorkspacePath is not null)
            {
                if (TrustWorkspaceCheckBox.IsChecked == true)
                {
                    roots.Add(_currentWorkspacePath);
                }
                else
                {
                    roots.Remove(_currentWorkspacePath);
                }
            }

            return new RocketToolSettings(
                NullIfWhiteSpace(CompilerPathTextBox.Text),
                NullIfWhiteSpace(LanguageServerPathTextBox.Text),
                roots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    private void BrowseCompiler_Click(object sender, RoutedEventArgs e) => BrowseInto(CompilerPathTextBox, "rocketc.exe");
    private void BrowseLanguageServer_Click(object sender, RoutedEventArgs e) => BrowseInto(LanguageServerPathTextBox, "rocket-lsp.exe");

    private void Automatic_Click(object sender, RoutedEventArgs e)
    {
        CompilerPathTextBox.Clear();
        LanguageServerPathTextBox.Clear();
        TrustWorkspaceCheckBox.IsChecked = false;
        _resetTrust = true;
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


    private static bool IsFileSystemRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeDirectory(string? path, bool requireExisting, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(path);
            var root = Path.GetPathRoot(normalized);
            if (!string.IsNullOrEmpty(root) && !string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return !requireExisting || Directory.Exists(normalized);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
