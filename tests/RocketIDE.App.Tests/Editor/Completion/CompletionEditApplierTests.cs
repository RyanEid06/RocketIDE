using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Editor.Completion;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Tests.Editor.Completion;

[TestClass]
public sealed class CompletionEditApplierTests
{
    [TestMethod]
    public void TryApply_AppliesPrimaryAndAdditionalEditsAgainstOriginalDocumentRanges()
    {
        var document = new TextDocument("fn main():\n    pri\n");
        var item = new RocketCompletionItem(
            "print",
            3,
            null,
            null,
            "print",
            null,
            null,
            new RocketCompletionTextEdit(
                new LspRange(new LspPosition(1, 4), new LspPosition(1, 7)),
                new LspRange(new LspPosition(1, 4), new LspPosition(1, 7)),
                "print"),
            new[]
            {
                new RocketTextEdit(new LspRange(new LspPosition(0, 0), new LspPosition(0, 0)), "import rocket.io\n"),
            });

        var applied = CompletionEditApplier.TryApply(document, item, fallbackOffset: 15, fallbackLength: 3, out var error);

        Assert.IsTrue(applied, error);
        Assert.AreEqual("import rocket.io\nfn main():\n    print\n", document.Text);
    }


    [TestMethod]
    public void TryApply_InsertReplaceEdit_UsesInsertRangeWithoutDeletingSuffix()
    {
        var document = new TextDocument("printable");
        var item = new RocketCompletionItem(
            "print",
            null,
            null,
            null,
            "print",
            null,
            null,
            new RocketCompletionTextEdit(
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 3)),
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 9)),
                "print"),
            Array.Empty<RocketTextEdit>());

        var applied = CompletionEditApplier.TryApply(document, item, 0, 3, out var error);

        Assert.IsTrue(applied, error);
        Assert.AreEqual("printntable", document.Text);
    }

    [TestMethod]
    public void TryApply_InsertReplaceEdit_RejectsInvalidReplaceRangeWithoutMutatingDocument()
    {
        const string original = "printable";
        var document = new TextDocument(original);
        var item = new RocketCompletionItem(
            "print",
            null,
            null,
            null,
            "print",
            null,
            null,
            new RocketCompletionTextEdit(
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 3)),
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 99)),
                "print"),
            Array.Empty<RocketTextEdit>());

        var applied = CompletionEditApplier.TryApply(document, item, 0, 3, out var error);

        Assert.IsFalse(applied);
        StringAssert.Contains(error, "replace range");
        Assert.AreEqual(original, document.Text);
    }

    [TestMethod]
    public void TryApply_RejectsOverlappingEditsWithoutMutatingDocument()
    {
        const string original = "abcdef";
        var document = new TextDocument(original);
        var item = new RocketCompletionItem(
            "x",
            null,
            null,
            null,
            "x",
            null,
            null,
            new RocketCompletionTextEdit(
                new LspRange(new LspPosition(0, 1), new LspPosition(0, 4)),
                new LspRange(new LspPosition(0, 1), new LspPosition(0, 4)),
                "x"),
            new[]
            {
                new RocketTextEdit(new LspRange(new LspPosition(0, 3), new LspPosition(0, 5)), "y"),
            });

        var applied = CompletionEditApplier.TryApply(document, item, 1, 3, out var error);

        Assert.IsFalse(applied);
        StringAssert.Contains(error, "overlap");
        Assert.AreEqual(original, document.Text);
    }

    [TestMethod]
    public void TryApply_RejectsUtf16RangeThatSplitsSurrogatePair()
    {
        const string original = "a🚀b";
        var document = new TextDocument(original);
        var item = new RocketCompletionItem(
            "x",
            null,
            null,
            null,
            "x",
            null,
            null,
            new RocketCompletionTextEdit(
                new LspRange(new LspPosition(0, 2), new LspPosition(0, 3)),
                new LspRange(new LspPosition(0, 2), new LspPosition(0, 3)),
                "x"),
            Array.Empty<RocketTextEdit>());

        Assert.IsFalse(CompletionEditApplier.TryApply(document, item, 0, 0, out _));
        Assert.AreEqual(original, document.Text);
    }
}
