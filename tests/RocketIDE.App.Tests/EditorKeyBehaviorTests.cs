using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Editor;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class EditorKeyBehaviorTests
{
    [TestMethod]
    public void TabAtDocumentEndMovesCaretOnceAndSharesText()
    {
        RunSta(() =>
        {
            var document = new TextDocument("main");
            var editor = new TextEditor { Document = document, CaretOffset = 4 };
            var other = new TextEditor { Document = document };

            Assert.IsTrue(EditorKeyBehavior.HandlePreviewKeyDown(editor, KeyEvent(Key.Tab)));

            Assert.AreEqual("main    ", other.Text);
            Assert.AreEqual(8, editor.CaretOffset);
        });
    }

    [TestMethod]
    public void BackspaceBetweenAutoPairMovesCaretOnce()
    {
        RunSta(() =>
        {
            var editor = new TextEditor { Text = "()", CaretOffset = 1 };
            Assert.IsTrue(EditorKeyBehavior.HandlePreviewKeyDown(editor, KeyEvent(Key.Back)));
            Assert.AreEqual("", editor.Text);
            Assert.AreEqual(0, editor.CaretOffset);
        });
    }

    private static KeyEventArgs KeyEvent(Key key) =>
        new(Keyboard.PrimaryDevice, new TestSource(), 0, key);

    private sealed class TestSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new DrawingVisual();
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    [TestMethod]
    public void HandleTextInput_AutoPairRelaysHandledTriggerText()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editor = new TextEditor { Document = new TextDocument("print") };
                editor.CaretOffset = editor.Document.TextLength;
                string? relayed = null;

                var handled = EditorKeyBehavior.HandleTextInput(editor, "(", text => relayed = text);

                Assert.IsTrue(handled);
                Assert.AreEqual("print()", editor.Document.Text);
                Assert.AreEqual(6, editor.CaretOffset);
                Assert.AreEqual("(", relayed);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            Assert.Fail(failure.ToString());
        }
    }

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
