using ICSharpCode.AvalonEdit;
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

    public static string CreateNewLineInsertion(string currentLineText) =>
        Environment.NewLine + new string(' ', RocketEditorRules.GetNewLineIndentation(currentLineText));
}
