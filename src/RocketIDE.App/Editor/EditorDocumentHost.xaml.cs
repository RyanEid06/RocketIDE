using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Editor;

public partial class EditorDocumentHost : UserControl
{
    public EditorDocumentHost()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.TextView.CurrentLineBackground = (Brush)FindResource("IDE.ChromeRaisedBrush");
    }

    public int EditorLineCount => Editor.Document.LineCount;

    public void Undo() => Editor.Undo();

    public void Redo() => Editor.Redo();

    public void SelectAll() => Editor.SelectAll();

    public void ShowFind(bool includeReplace)
    {
        if (string.IsNullOrEmpty(FindTextBox.Text) && !string.IsNullOrEmpty(Editor.SelectedText))
        {
            FindTextBox.Text = Editor.SelectedText;
        }

        FindBar.Visibility = Visibility.Visible;
        ReplaceControls.Visibility = includeReplace ? Visibility.Visible : Visibility.Collapsed;
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    public void GoToLine(int line)
    {
        var clampedLine = Math.Clamp(line, 1, Math.Max(1, Editor.Document.LineCount));
        var documentLine = Editor.Document.GetLineByNumber(clampedLine);
        Editor.TextArea.Caret.Offset = documentLine.Offset;
        Editor.ScrollTo(clampedLine, 1);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Editor.Document = e.NewValue is DocumentTabViewModel document
            ? document.EditorDocument
            : new TextDocument();
        ReportCaret();
    }

    private void Caret_PositionChanged(object? sender, EventArgs e) => ReportCaret();

    private void ReportCaret()
    {
        if (DataContext is DocumentTabViewModel document)
        {
            document.UpdateCaret(Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);
        }
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        FindNext(backwards: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext(backwards: false);

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindNext(backwards: true);

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var pattern = FindTextBox.Text;
        if (string.IsNullOrEmpty(pattern))
        {
            return;
        }

        var comparison = MatchCaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (string.Equals(Editor.SelectedText, pattern, comparison))
        {
            Editor.SelectedText = ReplaceTextBox.Text;
        }

        FindNext(backwards: false);
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var pattern = FindTextBox.Text;
        if (string.IsNullOrEmpty(pattern))
        {
            return;
        }

        var comparison = MatchCaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var replaced = Editor.Text.Replace(pattern, ReplaceTextBox.Text, comparison);
        if (string.Equals(replaced, Editor.Text, StringComparison.Ordinal))
        {
            return;
        }

        using (Editor.Document.RunUpdate())
        {
            Editor.Document.Replace(0, Editor.Document.TextLength, replaced);
        }
    }

    private void CloseFind_Click(object sender, RoutedEventArgs e) => CloseFind();

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ReplaceControls.Visibility = Visibility.Collapsed;
        Editor.Focus();
    }

    private void FindNext(bool backwards)
    {
        var pattern = FindTextBox.Text;
        var text = Editor.Text;
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text) || pattern.Length > text.Length)
        {
            return;
        }

        var comparison = MatchCaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var index = backwards
            ? FindPreviousIndex(text, pattern, comparison)
            : FindNextIndex(text, pattern, comparison);

        if (index < 0)
        {
            return;
        }

        Editor.Select(index, pattern.Length);
        var line = Editor.Document.GetLineByOffset(index).LineNumber;
        Editor.ScrollToLine(line);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    private int FindNextIndex(string text, string pattern, StringComparison comparison)
    {
        var start = Editor.SelectionLength > 0
            ? Editor.SelectionStart + Editor.SelectionLength
            : Editor.CaretOffset;

        var index = start <= text.Length - pattern.Length
            ? text.IndexOf(pattern, start, comparison)
            : -1;

        return index >= 0 ? index : text.IndexOf(pattern, comparison);
    }

    private int FindPreviousIndex(string text, string pattern, StringComparison comparison)
    {
        var start = Editor.SelectionLength > 0
            ? Editor.SelectionStart - 1
            : Math.Min(Editor.CaretOffset - 1, text.Length - 1);

        var index = start >= 0
            ? text.LastIndexOf(pattern, start, comparison)
            : -1;

        return index >= 0 ? index : text.LastIndexOf(pattern, comparison);
    }
}
