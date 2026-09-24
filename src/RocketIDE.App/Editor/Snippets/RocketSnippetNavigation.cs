using System.Windows.Input;

namespace RocketIDE.App.Editor.Snippets;

public static class RocketSnippetNavigation
{
    public static bool ShouldHandleTab(ModifierKeys modifiers) =>
        modifiers is ModifierKeys.None or ModifierKeys.Shift;
}
