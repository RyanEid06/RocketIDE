using System.IO;
using System.ComponentModel;
using System.Text;
using RocketIDE.App.Editor;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Documents;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class EditorTextOperationsPipelineTests
{
    [TestMethod]
    public void TextOperation_UsesSharedDocumentPipelineAndLeavesOtherDocumentUntouched()
    {
        var firstState = NewState("first.rocket", "value\n");
        var secondState = NewState("second.rocket", "other\n");
        var firstStore = new FakeDocumentStore(firstState);
        var secondStore = new FakeDocumentStore(secondState);
        var first = new DocumentTabViewModel(firstStore, firstState.Snapshot);
        var second = new DocumentTabViewModel(secondStore, secondState.Snapshot);
        var target = new FakeTarget();
        var versionChanges = 0;
        first.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentTabViewModel.Version))
            {
                versionChanges++;
            }
        };

        Assert.IsTrue(EditorTextOperations.ToggleLineComment(first.EditorDocument, target));

        Assert.AreEqual("# value\n", first.Text);
        Assert.IsTrue(first.IsDirty);
        Assert.AreEqual(1, first.Version);
        Assert.AreEqual(1, versionChanges);
        Assert.AreEqual("other\n", second.Text);
        Assert.IsFalse(second.IsDirty);
        Assert.AreEqual(0, second.Version);

        first.EditorDocument.UndoStack.Undo();
        Assert.AreEqual("value\n", first.Text);
        Assert.IsFalse(first.IsDirty);
    }

    private static DocumentState NewState(string fileName, string text)
    {
        var path = Path.GetFullPath(fileName);
        return new DocumentState(DocumentId.New(), path, text, Encoding.UTF8.GetByteCount(text));
    }

    private sealed class FakeDocumentStore(DocumentState state) : IDocumentStore
    {
        public IReadOnlyList<DocumentSnapshot> OpenDocuments => [state.Snapshot];
        public Task<DocumentSnapshot> OpenAsync(string path, CancellationToken cancellationToken) => Task.FromResult(state.Snapshot);
        public DocumentSnapshot UpdateText(DocumentId id, string text) => state.ApplyEdit(text);
        public Task<DocumentSnapshot> ReloadAsync(DocumentId id, CancellationToken cancellationToken) => Task.FromResult(state.Snapshot);
        public Task<DocumentSaveResult> SaveAsync(DocumentId id, bool overwriteExternalChanges, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DocumentSaveResult>> SaveAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public bool TryGet(DocumentId id, out DocumentSnapshot? document) { document = state.Snapshot; return true; }
        public bool Close(DocumentId id) => true;
    }

    private sealed class FakeTarget : IEditorCommandTarget
    {
        public int CaretLine => 1;
        public int CaretColumn => 1;
        public int CaretOffset { get; private set; }
        public int SelectionStart { get; private set; }
        public int SelectionLength { get; private set; }
        public void SetSelection(int startOffset, int length) { SelectionStart = startOffset; SelectionLength = length; CaretOffset = startOffset + length; }
        public void FocusEditor() { }
        public void Undo() { }
        public void Redo() { }
        public void SelectAll() { }
        public void ShowFind(bool includeReplace) { }
        public void GoToLine(int line) { }
    }
}
