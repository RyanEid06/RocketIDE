using System.IO;
using System.Text;
using RocketIDE.App.Editor;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Core.Documents;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class MultiPaneEditorIntegrationTests
{
    [TestMethod]
    public async Task OpenOrReveal_UsesExistingOtherGroupViewAndStoresViewLocalNavigation()
    {
        var document = CreateDocument("nav.rocket");
        var layout = new EditorLayoutViewModel();
        var left = layout.OpenOrActivate(document);
        var right = layout.SplitActive(EditorSplitOrientation.Vertical);
        layout.ActivateView(left);
        var integration = new MultiPaneEditorIntegration(layout, (_, _) => Task.FromResult<DocumentTabViewModel?>(document));
        var range = new SourceRange(2, 1, 2, 4);

        var resolved = await integration.OpenOrRevealAsync(document.Path, range, CancellationToken.None);

        Assert.AreSame(left, resolved);
        Assert.AreSame(document, integration.ActiveDocument);
        Assert.AreEqual(range, left.TakePendingNavigation());
        Assert.IsNull(right.TakePendingNavigation());
    }

    [TestMethod]
    public async Task OpenOrReveal_PropagatesCancellationBeforeOpening()
    {
        var layout = new EditorLayoutViewModel();
        var calls = 0;
        var integration = new MultiPaneEditorIntegration(layout, (_, _) =>
        {
            calls++;
            return Task.FromResult<DocumentTabViewModel?>(null);
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => integration.OpenOrRevealAsync("cancel.rocket", cancellationToken: cancellation.Token));

        Assert.AreEqual(0, calls);
    }

    private static DocumentTabViewModel CreateDocument(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), name));
        var id = DocumentId.New();
        var state = new DocumentState(id, path, "one\ntwo\nthree\n", Encoding.UTF8.GetByteCount("one\ntwo\nthree\n"));
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
