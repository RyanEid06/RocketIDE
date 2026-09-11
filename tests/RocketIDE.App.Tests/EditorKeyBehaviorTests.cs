using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Editor;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class EditorKeyBehaviorTests
{
    [TestMethod]
    public void GetPreferredNewLine_PreservesLfDocument()
    {
        var document = new TextDocument("first\nsecond\nthird");
        var caret = document.GetLineByNumber(2).Offset + 2;

        Assert.AreEqual("\n", RocketIndentationStrategy.GetPreferredNewLine(document, caret));
    }

    [TestMethod]
    public void GetPreferredNewLine_PreservesCrLfDocument()
    {
        var document = new TextDocument("first\r\nsecond\r\nthird");
        var caret = document.GetLineByNumber(2).Offset + 2;

        Assert.AreEqual("\r\n", RocketIndentationStrategy.GetPreferredNewLine(document, caret));
    }

    [TestMethod]
    public void GetPreferredNewLine_UsesNearestExistingDelimiterAtEndOfFile()
    {
        var document = new TextDocument("first\nsecond");

        Assert.AreEqual("\n", RocketIndentationStrategy.GetPreferredNewLine(document, document.TextLength));
    }

    [TestMethod]
    public void CreateNewLineInsertion_UsesProvidedDelimiterWithRocketIndentation()
    {
        Assert.AreEqual("\n    ", RocketIndentationStrategy.CreateNewLineInsertion("if ready:", "\n"));
        Assert.AreEqual("\r\n    ", RocketIndentationStrategy.CreateNewLineInsertion("if ready:", "\r\n"));
    }
}
