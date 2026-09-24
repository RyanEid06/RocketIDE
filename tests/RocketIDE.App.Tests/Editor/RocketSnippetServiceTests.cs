using System.IO;
using System.Text;
using RocketIDE.App.Editor;
using RocketIDE.App.Editor.Snippets;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Documents;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class RocketSnippetServiceTests
{
    [TestMethod]
    public void Expand_PreservesPlaceholderOrderAndFinalStop()
    {
        var expansion = RocketSnippetService.Expand("fn ${1:name}(${2:value}) -> ${3:Int}:\n    $0");

        Assert.AreEqual("fn name(value) -> Int:\n    ", expansion.Text);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 0 }, expansion.Placeholders.Select(item => item.Index).ToArray());
    }

    [TestMethod]
    public void Catalog_HasUniqueTriggers()
    {
        var triggers = RocketSnippetCatalog.Default.Select(item => item.Trigger).ToArray();

        Assert.AreEqual(triggers.Length, triggers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        CollectionAssert.Contains(triggers, "main");
        CollectionAssert.Contains(triggers, "fn");
        CollectionAssert.Contains(triggers, "impl");
    }
    [TestMethod]
    public void Insert_UsesSharedTextDocumentAndOneUndoGroup()
    {
        var id = DocumentId.New();
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "snippet.rocket"));
        var state = new DocumentState(id, path, "x", Encoding.UTF8.GetByteCount("x"));
        var document = new DocumentTabViewModel(new Store(state), state.Snapshot);
        var view = new EditorViewViewModel(document);
        var target = new Target();
        view.AttachCommandTarget(target);
        target.SelectionStartValue = 0;
        target.SelectionLengthValue = 1;
        var snippet = new RocketSnippetDefinition("Value", "value", "test", ["${1:value}$0"]);

        new RocketSnippetService().Insert(view, snippet);

        Assert.AreEqual("value", document.EditorDocument.Text);
        Assert.IsTrue(document.IsDirty);
        document.EditorDocument.UndoStack.Undo();
        Assert.AreEqual("x", document.EditorDocument.Text);
    }

    private sealed class Target : IEditorCommandTarget
    {
        public int SelectionStartValue { get; set; }
        public int SelectionLengthValue { get; set; }
        public int CaretLine => 1;
        public int CaretColumn => 1;
        public int CaretOffset => SelectionStartValue;
        public int SelectionStart => SelectionStartValue;
        public int SelectionLength => SelectionLengthValue;
        public void SetSelection(int startOffset, int length) { SelectionStartValue = startOffset; SelectionLengthValue = length; }
        public void FocusEditor() { }
        public void Undo() { }
        public void Redo() { }
        public void SelectAll() { }
        public void ShowFind(bool includeReplace) { }
        public void GoToLine(int line) { }
    }

    private sealed class Store(DocumentState state) : IDocumentStore
    {
        public IReadOnlyList<DocumentSnapshot> OpenDocuments => [state.Snapshot];
        public Task<DocumentSnapshot> OpenAsync(string path, CancellationToken cancellationToken) => Task.FromResult(state.Snapshot);
        public DocumentSnapshot UpdateText(DocumentId id, string text) => state.ApplyEdit(text);
        public Task<DocumentSnapshot> ReloadAsync(DocumentId id, CancellationToken cancellationToken) => Task.FromResult(state.Snapshot);
        public Task<DocumentSaveResult> SaveAsync(DocumentId id, bool overwriteExternalChanges, CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentSaveResult(DocumentSaveStatus.NoChanges, state.Snapshot));
        public Task<IReadOnlyList<DocumentSaveResult>> SaveAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentSaveResult>>([]);
        public bool TryGet(DocumentId id, out DocumentSnapshot? document) { document = state.Snapshot; return true; }
        public bool Close(DocumentId id) => true;
    }

}
