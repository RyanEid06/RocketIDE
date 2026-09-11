using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using RocketIDE.Rocket.Language;

namespace RocketIDE.App.Editor;

public static class EditorKeyBehavior
{
    public static bool HandleTextInput(TextEditor editor, string text)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (string.IsNullOrEmpty(text) || text.Length != 1)
        {
            return false;
        }

        var typed = text[0];
        if (editor.SelectionLength == 0 && (typed is ')' or ']' or '"' or '\'') &&
            editor.CaretOffset < editor.Document.TextLength &&
            editor.Document.GetCharAt(editor.CaretOffset) == typed)
        {
            editor.CaretOffset++;
            return true;
        }

        var close = RocketEditorRules.GetClosingCharacter(typed);
        if (close is not null)
        {
            if (typed is '"' or '\'' && IsInStringOrComment(editor))
            {
                return false;
            }

            if (editor.SelectionLength > 0)
            {
                var start = editor.SelectionStart;
                var selected = editor.SelectedText;
                editor.Document.Replace(start, editor.SelectionLength, RocketEditorRules.WrapSelection(selected, typed));
                editor.Select(start + 1, selected.Length);
                editor.CaretOffset = start + 1 + selected.Length;
                return true;
            }

            editor.Document.Insert(editor.CaretOffset, string.Concat(typed, close.Value));
            editor.CaretOffset++;
            return true;
        }

        return false;
    }

    public static bool HandlePreviewKeyDown(TextEditor editor, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key == Key.Enter)
        {
            var caretOffset = editor.CaretOffset;
            var line = editor.Document.GetLineByOffset(caretOffset);
            var textBeforeCaret = editor.Document.GetText(line.Offset, Math.Max(0, caretOffset - line.Offset));
            var newLine = RocketIndentationStrategy.GetPreferredNewLine(editor.Document, caretOffset);

            if (editor.SelectionLength == 0)
            {
                var previousNonBlankLine = GetPreviousNonBlankLine(editor, line.LineNumber);
                var dedent = RocketEditorRules.GetCurrentLineDedent(textBeforeCaret, previousNonBlankLine);
                if (dedent > 0 && textBeforeCaret.StartsWith(new string(' ', dedent), StringComparison.Ordinal))
                {
                    var adjustedLinePrefix = textBeforeCaret[dedent..];
                    var insertion = RocketIndentationStrategy.CreateNewLineInsertion(adjustedLinePrefix, newLine);
                    var replacement = adjustedLinePrefix + insertion;
                    editor.Document.Replace(line.Offset, caretOffset - line.Offset, replacement);
                    editor.CaretOffset = line.Offset + replacement.Length;
                    return true;
                }
            }

            var normalInsertion = RocketIndentationStrategy.CreateNewLineInsertion(textBeforeCaret, newLine);
            var selectionStart = editor.SelectionStart;
            editor.Document.Replace(selectionStart, editor.SelectionLength, normalInsertion);
            editor.CaretOffset = selectionStart + normalInsertion.Length;
            return true;
        }

        if (e.Key == Key.Tab)
        {
            ApplyIndent(editor, outdent: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return true;
        }

        if (e.Key == Key.Back && editor.SelectionLength == 0 && editor.CaretOffset > 0 && editor.CaretOffset < editor.Document.TextLength)
        {
            var before = editor.Document.GetCharAt(editor.CaretOffset - 1);
            var after = editor.Document.GetCharAt(editor.CaretOffset);
            if (RocketEditorRules.IsAutoClosePair(before, after))
            {
                editor.Document.Remove(editor.CaretOffset - 1, 2);
                editor.CaretOffset--;
                return true;
            }
        }

        return false;
    }

    private static void ApplyIndent(TextEditor editor, bool outdent)
    {
        const string indent = "    ";
        if (editor.SelectionLength == 0)
        {
            if (!outdent)
            {
                editor.Document.Insert(editor.CaretOffset, indent);
                editor.CaretOffset += indent.Length;
            }
            else
            {
                var line = editor.Document.GetLineByOffset(editor.CaretOffset);
                var removable = Math.Min(indent.Length, editor.CaretOffset - line.Offset);
                var start = editor.CaretOffset - removable;
                var text = editor.Document.GetText(start, removable);
                var spaces = text.Reverse().TakeWhile(character => character == ' ').Count();
                if (spaces > 0)
                {
                    editor.Document.Remove(editor.CaretOffset - spaces, spaces);
                    editor.CaretOffset -= spaces;
                }
            }

            return;
        }

        var selectionStart = editor.SelectionStart;
        var selectionEnd = selectionStart + editor.SelectionLength;
        var firstLine = editor.Document.GetLineByOffset(selectionStart).LineNumber;
        var lastOffset = Math.Max(selectionStart, selectionEnd - 1);
        var lastLine = editor.Document.GetLineByOffset(lastOffset).LineNumber;

        using (editor.Document.RunUpdate())
        {
            for (var lineNumber = lastLine; lineNumber >= firstLine; lineNumber--)
            {
                var line = editor.Document.GetLineByNumber(lineNumber);
                if (!outdent)
                {
                    editor.Document.Insert(line.Offset, indent);
                    continue;
                }

                var text = editor.Document.GetText(line.Offset, Math.Min(indent.Length, line.Length));
                var spaces = text.TakeWhile(character => character == ' ').Count();
                if (spaces > 0)
                {
                    editor.Document.Remove(line.Offset, spaces);
                }
            }
        }
    }

    private static string? GetPreviousNonBlankLine(TextEditor editor, int currentLineNumber)
    {
        for (var lineNumber = currentLineNumber - 1; lineNumber >= 1; lineNumber--)
        {
            var line = editor.Document.GetLineByNumber(lineNumber);
            var text = editor.Document.GetText(line.Offset, line.Length);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool IsInStringOrComment(TextEditor editor)
    {
        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        var lineText = editor.Document.GetText(line.Offset, line.Length);
        return RocketEditorRules.IsInStringOrComment(lineText, editor.CaretOffset - line.Offset);
    }
}
