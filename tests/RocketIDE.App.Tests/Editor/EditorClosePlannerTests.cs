using System.IO;
using System.Text;
using RocketIDE.App.Editor;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Documents;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class EditorClosePlannerTests
{
    [TestMethod]
    public void ClosingOneOfTwoViews_DoesNotCloseLogicalDocument()
    {
        var document = CreateDocument();
        var layout = new EditorLayoutViewModel();
        var first = layout.OpenOrActivate(document);
        layout.SplitActive(EditorSplitOrientation.Vertical);

        var closing = EditorClosePlanner.LogicalDocumentsClosing(layout, [first]);

        Assert.AreEqual(0, closing.Count);
    }

    [TestMethod]
    public void ClosingBothViews_ClosesLogicalDocumentExactlyOnce()
    {
        var document = CreateDocument();
        var layout = new EditorLayoutViewModel();
        layout.OpenOrActivate(document);
        layout.SplitActive(EditorSplitOrientation.Vertical);

        var closing = EditorClosePlanner.LogicalDocumentsClosing(
            layout,
            layout.Groups.SelectMany(group => group.Views).ToArray());

        Assert.AreEqual(1, closing.Count);
        Assert.AreSame(document, closing[0]);
    }

    private static DocumentTabViewModel CreateDocument()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "close.rocket"));
        var id = DocumentId.New();
        var state = new DocumentState(id, path, "value", Encoding.UTF8.GetByteCount("value"));
        return new DocumentTabViewModel(new Store(state), state.Snapshot);
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
