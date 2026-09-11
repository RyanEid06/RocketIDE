using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.Rocket.Language;

namespace RocketIDE.App.Editor;

public static class RocketIndentationStrategy
{
    public static void Configure(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = RocketEditorRules.IndentationSize;
    }

    public static string CreateNewLineInsertion(string currentLineText, string newLine) =>
        newLine + new string(' ', RocketEditorRules.GetNewLineIndentation(currentLineText));

    public static string GetPreferredNewLine(TextDocument document, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (caretOffset < 0 || caretOffset > document.TextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(caretOffset));
        }

        var line = document.GetLineByOffset(caretOffset);
        for (var current = line; current is not null; current = current.PreviousLine)
        {
            if (current.DelimiterLength > 0)
            {
                return document.GetText(current.EndOffset, current.DelimiterLength);
            }
        }

        for (var current = line.NextLine; current is not null; current = current.NextLine)
        {
            if (current.DelimiterLength > 0)
            {
                return document.GetText(current.EndOffset, current.DelimiterLength);
            }
        }

        return Environment.NewLine;
    }
}
