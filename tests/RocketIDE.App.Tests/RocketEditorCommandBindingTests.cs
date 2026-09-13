using System.IO;
using System.Windows.Input;
using RocketIDE.App.Editor;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class RocketEditorCommandBindingTests
{
    [TestMethod]
    [DataRow(Key.F12, ModifierKeys.None, RocketEditorCommand.Definition)]
    [DataRow(Key.F12, ModifierKeys.Shift, RocketEditorCommand.References)]
    [DataRow(Key.F2, ModifierKeys.None, RocketEditorCommand.Rename)]
    [DataRow(Key.OemPeriod, ModifierKeys.Control, RocketEditorCommand.CodeActions)]
    [DataRow(Key.F, ModifierKeys.Shift | ModifierKeys.Alt, RocketEditorCommand.FormatDocument)]
    public void TryGetCommand_MapsWp09Shortcuts(Key key, ModifierKeys modifiers, RocketEditorCommand expected)
    {
        Assert.IsTrue(RocketEditorCommandBinding.TryGetCommand(key, modifiers, out var command));
        Assert.AreEqual(expected, command);
    }

    [TestMethod]
    public void Wp09MainMenuItems_UseExplicitDarkMenuStyleInsidePopup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RocketIDE.sln")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "Could not locate the RocketIDE repository root from the test output directory.");
        var xaml = File.ReadAllText(Path.Combine(directory.FullName, "src", "RocketIDE.App", "MainWindow.xaml"));
        var requiredItems = new[]
        {
            "Header=\"_Go to Definition\"",
            "Header=\"Find _References\"",
            "Header=\"Rename _Symbol...\"",
            "Header=\"_Quick Fixes (server-provided)\"",
            "Header=\"_Format Document\"",
        };

        foreach (var header in requiredItems)
        {
            var headerIndex = xaml.IndexOf(header, StringComparison.Ordinal);
            Assert.IsTrue(headerIndex >= 0, $"Missing WP09 menu item {header}.");
            var itemStart = xaml.LastIndexOf("<MenuItem", headerIndex, StringComparison.Ordinal);
            Assert.IsTrue(itemStart >= 0, $"Could not locate MenuItem start for {header}.");
            var itemEnd = xaml.IndexOf('>', headerIndex);
            Assert.IsTrue(itemEnd > headerIndex, $"Could not locate MenuItem end for {header}.");
            var openingTag = xaml[itemStart..(itemEnd + 1)];
            StringAssert.Contains(openingTag, "Style=\"{StaticResource IDE.MenuItemStyle}\"");
        }
    }
    [TestMethod]
    public void DarkContextMenu_UsesCustomTemplateWithoutSystemIconGutter()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RocketIDE.sln")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "Could not locate the RocketIDE repository root from the test output directory.");
        var xaml = File.ReadAllText(Path.Combine(directory.FullName, "src", "RocketIDE.App", "Themes", "DarkTheme.xaml"));

        StringAssert.Contains(xaml, "<ControlTemplate x:Key=\"IDE.ContextMenuTemplate\" TargetType=\"{x:Type ContextMenu}\">");
        StringAssert.Contains(xaml, "<Setter Property=\"Template\" Value=\"{StaticResource IDE.ContextMenuTemplate}\" />");
    }

}
