using System.Windows;
using System.Windows.Controls;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Editor;

public sealed class EditorViewEventArgs(EditorViewViewModel view) : EventArgs
{
    public EditorViewViewModel View { get; } = view ?? throw new ArgumentNullException(nameof(view));
}

public partial class EditorGroupHost : UserControl
{
    public EditorGroupHost() => InitializeComponent();

    public event EventHandler<EditorViewEventArgs>? ViewCloseRequested;
    public event EventHandler<EditorViewEventArgs>? ViewActivated;
    public event EventHandler<RocketEditorCommandRequestedEventArgs>? RocketCommandRequested;

    private void CloseView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EditorViewViewModel view })
            ViewCloseRequested?.Invoke(this, new EditorViewEventArgs(view));
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is EditorViewViewModel view)
            ViewActivated?.Invoke(this, new EditorViewEventArgs(view));
    }

    private void EditorHost_ViewActivated(object? sender, EventArgs e)
    {
        if (sender is EditorDocumentHost { DataContext: EditorViewViewModel view })
            ViewActivated?.Invoke(this, new EditorViewEventArgs(view));
    }

    private void EditorHost_RocketCommandRequested(object? sender, RocketEditorCommandRequestedEventArgs e) =>
        RocketCommandRequested?.Invoke(sender ?? this, e);
}
