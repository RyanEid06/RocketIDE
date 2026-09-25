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
    public void Catalog_HasUniqueTriggers_AndFinalStops()
    {
        var triggers = RocketSnippetCatalog.Default.Select(item => item.Trigger).ToArray();

        Assert.AreEqual(triggers.Length, triggers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        CollectionAssert.Contains(triggers, "main");
        CollectionAssert.Contains(triggers, "fn");
        CollectionAssert.Contains(triggers, "impl");
        Assert.IsTrue(RocketSnippetCatalog.Default.All(snippet => string.Join("\n", snippet.Body).Contains("$0", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void NavigationTab_RejectsCtrlAltAndWindowsModifiers()
    {
        Assert.IsTrue(RocketSnippetNavigation.ShouldHandleTab(System.Windows.Input.ModifierKeys.None));
        Assert.IsTrue(RocketSnippetNavigation.ShouldHandleTab(System.Windows.Input.ModifierKeys.Shift));
        Assert.IsFalse(RocketSnippetNavigation.ShouldHandleTab(System.Windows.Input.ModifierKeys.Control));
        Assert.IsFalse(RocketSnippetNavigation.ShouldHandleTab(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));
        Assert.IsFalse(RocketSnippetNavigation.ShouldHandleTab(System.Windows.Input.ModifierKeys.Alt));
        Assert.IsFalse(RocketSnippetNavigation.ShouldHandleTab(System.Windows.Input.ModifierKeys.Windows));
    }

    [TestMethod]
    public void Insert_UsesSharedTextDocumentAndOneUndoGroup()
    {
        var document = CreateDocument("snippet.rocket", "x");
        var view = new EditorViewViewModel(document);
        var target = new Target { SelectionStartValue = 0, SelectionLengthValue = 1 };
        view.AttachCommandTarget(target);
        var snippet = new RocketSnippetDefinition("Value", "value", "test", ["${1:value}$0"]);

        new RocketSnippetService().Insert(view, snippet);

        Assert.AreEqual("value", document.EditorDocument.Text);
        Assert.IsTrue(document.IsDirty);
        document.EditorDocument.UndoStack.Undo();
        Assert.AreEqual("x", document.EditorDocument.Text);
    }

    [TestMethod]
    public void Insert_RejectsNonRocketDocument()
    {
        var document = CreateDocument("notes.txt", "x");
        var view = new EditorViewViewModel(document);
        view.AttachCommandTarget(new Target());

        Assert.IsFalse(RocketSnippetService.CanInsert(view));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new RocketSnippetService().Insert(view, RocketSnippetCatalog.Default[0]));
    }

    [TestMethod]
    public void RepeatedPlaceholder_IsSynchronizedBeforeAdvancing()
    {
        var document = CreateDocument("snippet.rocket", string.Empty);
        var view = new EditorViewViewModel(document);
        var target = new Target();
        view.AttachCommandTarget(target);
        var snippet = new RocketSnippetDefinition(
            "Linked",
            "linked",
            "linked placeholder",
            ["impl ${1:Type}:", "    fn run(self: ${1:Type}) -> Int:", "        return 0$0"]);

        var session = new RocketSnippetService().Insert(view, snippet);
        document.EditorDocument.Replace(target.SelectionStart, target.SelectionLength, "Widget");

        Assert.IsTrue(session.MoveNext());
        StringAssert.Contains(document.EditorDocument.Text, "impl Widget:");
        StringAssert.Contains(document.EditorDocument.Text, "self: Widget");
    }

    [TestMethod]
    public void DetachingCommandTarget_CancelsSnippetSession()
    {
        var document = CreateDocument("snippet.rocket", string.Empty);
        var view = new EditorViewViewModel(document);
        var target = new Target();
        view.AttachCommandTarget(target);

        var session = new RocketSnippetService().Insert(
            view,
            new RocketSnippetDefinition("Value", "value", "test", ["${1:value}$0"]));

        Assert.IsTrue(session.IsActive);
        view.DetachCommandTarget(target);

        Assert.IsFalse(session.IsActive);
        Assert.IsNull(view.SnippetSession);
        Assert.IsNull(view.CommandTarget);
    }

    private static DocumentTabViewModel CreateDocument(string name, string text)
    {
        var id = DocumentId.New();
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), name));
        var state = new DocumentState(id, path, text, Encoding.UTF8.GetByteCount(text));
        return new DocumentTabViewModel(new Store(state), state.Snapshot);
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
