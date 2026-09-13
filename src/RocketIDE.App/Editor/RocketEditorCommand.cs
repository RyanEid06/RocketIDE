using System.Windows.Input;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Editor;

public enum RocketEditorCommand
{
    Definition,
    References,
    Rename,
    CodeActions,
    FormatDocument,
}

public sealed class RocketEditorCommandRequestedEventArgs(
    RocketEditorCommand command,
    string path,
    LspPosition position,
    LspRange range) : EventArgs
{
    public RocketEditorCommand Command { get; } = command;
    public string Path { get; } = path;
    public LspPosition Position { get; } = position;
    public LspRange Range { get; } = range;
}

public static class RocketEditorCommandBinding
{
    public static bool TryGetCommand(Key key, ModifierKeys modifiers, out RocketEditorCommand command)
    {
        command = default;
        if (key == Key.F12 && modifiers == ModifierKeys.None) { command = RocketEditorCommand.Definition; return true; }
        if (key == Key.F12 && modifiers == ModifierKeys.Shift) { command = RocketEditorCommand.References; return true; }
        if (key == Key.F2 && modifiers == ModifierKeys.None) { command = RocketEditorCommand.Rename; return true; }
        if (key == Key.OemPeriod && modifiers == ModifierKeys.Control) { command = RocketEditorCommand.CodeActions; return true; }
        if (key == Key.F && modifiers == (ModifierKeys.Shift | ModifierKeys.Alt)) { command = RocketEditorCommand.FormatDocument; return true; }
        return false;
    }
}
