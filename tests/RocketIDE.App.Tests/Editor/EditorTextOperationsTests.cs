using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Editor;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class EditorTextOperationsTests
{
    [TestMethod]
    public void ToggleLineComment_CurrentLine_CommentsAndUncommentsInSingleUndoUnits()
    {
        var document = NewDocument("    let value = 1\nnext");
        var target = new FakeTarget(caret: 8);

        Assert.IsTrue(EditorTextOperations.ToggleLineComment(document, target));
        Assert.AreEqual("    # let value = 1\nnext", document.Text);
        Assert.AreEqual(10, target.CaretOffset);

        document.UndoStack.Undo();
        Assert.AreEqual("    let value = 1\nnext", document.Text);
        document.UndoStack.Redo();
        Assert.AreEqual("    # let value = 1\nnext", document.Text);

        Assert.IsTrue(EditorTextOperations.ToggleLineComment(document, target));
        Assert.AreEqual("    let value = 1\nnext", document.Text);
    }

    [TestMethod]
    public void ToggleLineComment_MultilineSelectionEndingAtNextLineStart_DoesNotTouchTrailingLine()
    {
        var document = NewDocument("one\ntwo\nthree");
        var target = new FakeTarget(caret: 8, selectionStart: 0, selectionLength: 8);

        Assert.IsTrue(EditorTextOperations.ToggleLineComment(document, target));

        Assert.AreEqual("# one\n# two\nthree", document.Text);
        Assert.AreEqual("one\ntwo\n", document.GetText(target.SelectionStart, target.SelectionLength).Replace("# ", string.Empty));
    }

    [TestMethod]
    public void ToggleLineComment_BlankOnlySelection_IsNoOp()
    {
        var document = NewDocument("   \n\n");
        var target = new FakeTarget(caret: 1, selectionStart: 0, selectionLength: document.TextLength);
        var beforeUndo = document.UndoStack.CanUndo;

        Assert.IsFalse(EditorTextOperations.ToggleLineComment(document, target));

        Assert.AreEqual("   \n\n", document.Text);
        Assert.AreEqual(beforeUndo, document.UndoStack.CanUndo);
    }

    [TestMethod]
    public void DuplicateSelection_DuplicatesExactUnicodeSelectionAndSelectsDuplicate()
    {
        var document = NewDocument("a😀b");
        var target = new FakeTarget(caret: 3, selectionStart: 1, selectionLength: 2);

        Assert.IsTrue(EditorTextOperations.DuplicateLineOrSelection(document, target));

        Assert.AreEqual("a😀😀b", document.Text);
        Assert.AreEqual(3, target.SelectionStart);
        Assert.AreEqual(2, target.SelectionLength);
        document.UndoStack.Undo();
        Assert.AreEqual("a😀b", document.Text);
    }

    [TestMethod]
    public void DuplicateLine_PreservesCrLfAndFinalLineWithoutDelimiter()
    {
        var document = NewDocument("one\r\ntwo");
        var target = new FakeTarget(caret: document.TextLength);

        Assert.IsTrue(EditorTextOperations.DuplicateLineOrSelection(document, target));

        Assert.AreEqual("one\r\ntwo\r\ntwo", document.Text);
        document.UndoStack.Undo();
        Assert.AreEqual("one\r\ntwo", document.Text);
    }

    [TestMethod]
    public void EmptyDocument_DuplicateCreatesSecondEmptyLineAndMoveIsNoOp()
    {
        var document = NewDocument(string.Empty);
        var target = new FakeTarget(caret: 0);

        Assert.IsTrue(EditorTextOperations.DuplicateLineOrSelection(document, target));
        Assert.AreEqual(Environment.NewLine, document.Text);
        document.UndoStack.Undo();
        Assert.AreEqual(string.Empty, document.Text);

        Assert.IsFalse(EditorTextOperations.MoveLineOrSelectionUp(document, target));
        Assert.IsFalse(EditorTextOperations.MoveLineOrSelectionDown(document, target));
    }

    [TestMethod]
    public void MoveSelectionUp_PreservesPartialSelectionTextAndLf()
    {
        var document = NewDocument("alpha\nbeta\ngamma");
        var target = new FakeTarget(caret: 9, selectionStart: 7, selectionLength: 2);
        var selected = document.GetText(target.SelectionStart, target.SelectionLength);

        Assert.IsTrue(EditorTextOperations.MoveLineOrSelectionUp(document, target));

        Assert.AreEqual("beta\nalpha\ngamma", document.Text);
        Assert.AreEqual(selected, document.GetText(target.SelectionStart, target.SelectionLength));
        document.UndoStack.Undo();
        Assert.AreEqual("alpha\nbeta\ngamma", document.Text);
    }

    [TestMethod]
    public void MoveSelectionDown_PreservesCrLfAndSelection()
    {
        var document = NewDocument("one\r\ntwo\r\nthree");
        var target = new FakeTarget(caret: 6, selectionStart: 5, selectionLength: 3);
        var selected = document.GetText(target.SelectionStart, target.SelectionLength);

        Assert.IsTrue(EditorTextOperations.MoveLineOrSelectionDown(document, target));

        Assert.AreEqual("one\r\nthree\r\ntwo", document.Text);
        Assert.AreEqual(selected, document.GetText(target.SelectionStart, target.SelectionLength));
        document.UndoStack.Undo();
        Assert.AreEqual("one\r\ntwo\r\nthree", document.Text);
    }

    [TestMethod]
    public void MoveLine_BoundariesAreCleanNoOps()
    {
        var document = NewDocument("one\ntwo");
        var first = new FakeTarget(caret: 1);
        var last = new FakeTarget(caret: document.TextLength);

        Assert.IsFalse(EditorTextOperations.MoveLineOrSelectionUp(document, first));
        Assert.IsFalse(EditorTextOperations.MoveLineOrSelectionDown(document, last));
        Assert.AreEqual("one\ntwo", document.Text);
    }

    [TestMethod]
    public void DeleteSelection_DeletesExactlySelectionAndUndoesOnce()
    {
        var document = NewDocument("abc def");
        var target = new FakeTarget(caret: 7, selectionStart: 4, selectionLength: 3);

        Assert.IsTrue(EditorTextOperations.DeleteLineOrSelection(document, target));

        Assert.AreEqual("abc ", document.Text);
        Assert.AreEqual(4, target.CaretOffset);
        document.UndoStack.Undo();
        Assert.AreEqual("abc def", document.Text);
    }

    [TestMethod]
    public void DeleteLastLine_RemovesPrecedingDelimiterWithoutLeavingTrailingBlankLine()
    {
        var document = NewDocument("one\r\ntwo");
        var target = new FakeTarget(caret: document.TextLength);

        Assert.IsTrue(EditorTextOperations.DeleteLineOrSelection(document, target));

        Assert.AreEqual("one", document.Text);
        document.UndoStack.Undo();
        Assert.AreEqual("one\r\ntwo", document.Text);
    }

    [TestMethod]
    public void DeleteLine_EmptyDocument_IsNoOp()
    {
        var document = NewDocument(string.Empty);
        var target = new FakeTarget(caret: 0);

        Assert.IsFalse(EditorTextOperations.DeleteLineOrSelection(document, target));
        Assert.AreEqual(string.Empty, document.Text);
    }

    [TestMethod]
    public void OperationRejectsSelectionThatSplitsSurrogatePair()
    {
        var document = NewDocument("a😀b");
        var target = new FakeTarget(caret: 2, selectionStart: 2, selectionLength: 0);

        Assert.IsFalse(EditorTextOperations.DeleteLineOrSelection(document, target));
        Assert.AreEqual("a😀b", document.Text);
    }

    private static TextDocument NewDocument(string text)
    {
        var document = new TextDocument(text);
        document.UndoStack.ClearAll();
        return document;
    }

    private sealed class FakeTarget : IEditorCommandTarget
    {
        public FakeTarget(int caret, int selectionStart = -1, int selectionLength = 0)
        {
            CaretOffset = caret;
            SelectionStart = selectionStart < 0 ? caret : selectionStart;
            SelectionLength = selectionLength;
        }

        public int CaretLine => 1;
        public int CaretColumn => CaretOffset + 1;
        public int CaretOffset { get; private set; }
        public int SelectionStart { get; private set; }
        public int SelectionLength { get; private set; }
        public int FocusCount { get; private set; }

        public void SetSelection(int startOffset, int length)
        {
            SelectionStart = startOffset;
            SelectionLength = length;
            CaretOffset = startOffset + length;
        }

        public void FocusEditor() => FocusCount++;
        public void Undo() { }
        public void Redo() { }
        public void SelectAll() { }
        public void ShowFind(bool includeReplace) { }
        public void GoToLine(int line) { }
    }
}
