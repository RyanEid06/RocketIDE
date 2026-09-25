using System.IO;
using System.Text;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Documents;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Infrastructure.Files;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class EditorLayoutViewModelTests
{
    [TestMethod]
    public void SplitActive_UsesSameLogicalDocumentAndSameTextDocument()
    {
        var document = CreateDocument("split.rocket");
        var layout = new EditorLayoutViewModel();
        var first = layout.OpenOrActivate(document);

        var second = layout.SplitActive(EditorSplitOrientation.Vertical);

        Assert.AreEqual(2, layout.Groups.Count);
        Assert.AreSame(first.Document, second.Document);
        Assert.AreSame(first.Document.EditorDocument, second.Document.EditorDocument);
        Assert.AreEqual(2, layout.CountViews(document));
    }

    [TestMethod]
    public void OpenOrActivate_ExistingViewInOtherGroupWins()
    {
        var firstDocument = CreateDocument("first.rocket");
        var secondDocument = CreateDocument("second.rocket");
        var layout = new EditorLayoutViewModel();
        var left = layout.OpenOrActivate(firstDocument);
        var right = layout.SplitActive(EditorSplitOrientation.Vertical);
        layout.OpenOrActivate(secondDocument);

        var resolved = layout.OpenOrActivate(firstDocument);

        Assert.AreSame(right, resolved);
        Assert.AreSame(firstDocument, resolved.Document);
        Assert.AreSame(layout.Groups[1], layout.ActiveGroup);
        Assert.AreEqual(2, layout.CountViews(firstDocument));
        Assert.AreSame(left.Document.EditorDocument, right.Document.EditorDocument);
    }

    [TestMethod]
    public void SameDocumentViewsKeepIndependentViewState()
    {
        var document = CreateDocument("state.rocket");
        var layout = new EditorLayoutViewModel();
        var first = layout.OpenOrActivate(document);
        var second = layout.SplitActive(EditorSplitOrientation.Vertical);

        first.UpdateCaret(2, 3, 4);
        first.UpdateSelection(1, 2);
        first.UpdateScroll(10, 20);
        second.UpdateCaret(8, 9, 10);
        second.UpdateSelection(7, 3);
        second.UpdateScroll(30, 40);

        Assert.AreEqual(4, first.CaretOffset);
        Assert.AreEqual(10, second.CaretOffset);
        Assert.AreEqual(1, first.SelectionStart);
        Assert.AreEqual(7, second.SelectionStart);
        Assert.AreEqual(20d, first.VerticalOffset);
        Assert.AreEqual(40d, second.VerticalOffset);
    }

    [TestMethod]
    public void CaptureStateReadsCurrentFoldPresentationFromEachView()
    {
        var document = CreateDocument("fold-state.rocket");
        var layout = new EditorLayoutViewModel();
        var first = layout.OpenOrActivate(document);
        var second = layout.SplitActive(EditorSplitOrientation.Vertical);
        first.SetFoldingStateCapture(() => [new SourceRange(0, 0, 3, 0)]);
        second.SetFoldingStateCapture(() => []);

        var firstState = first.CaptureState();
        var secondState = second.CaptureState();

        Assert.AreEqual(1, firstState.CollapsedFolds.Count);
        Assert.AreEqual(0, secondState.CollapsedFolds.Count);
        Assert.AreSame(first.Document.EditorDocument, second.Document.EditorDocument);
    }

    [TestMethod]
    public void SwitchingSameDocumentViewsRaisesActiveContextChanged()
    {
        var document = CreateDocument("context.rocket");
        var layout = new EditorLayoutViewModel();
        var first = layout.OpenOrActivate(document);
        var second = layout.SplitActive(EditorSplitOrientation.Vertical);
        var changes = 0;
        layout.ActiveContextChanged += (_, _) => changes++;

        layout.ActivateView(first);
        layout.ActivateView(second);

        Assert.IsTrue(changes >= 2);
        Assert.AreSame(document, layout.ActiveView?.Document);
    }

    [TestMethod]
    public void MoveActiveToOtherGroup_DeduplicatesDocumentAndCollapsesEmptyGroup()
    {
        var document = CreateDocument("move.rocket");
        var layout = new EditorLayoutViewModel();
        layout.OpenOrActivate(document);
        layout.SplitActive(EditorSplitOrientation.Vertical);

        Assert.IsTrue(layout.MoveActiveToOtherGroup());

        Assert.AreEqual(1, layout.Groups.Count);
        Assert.AreEqual(EditorSplitOrientation.None, layout.Orientation);
        Assert.AreEqual(1, layout.CountViews(document));
    }


    [TestMethod]
    public void SplitActive_NeverCreatesThirdGroupOrDuplicateInSameGroup()
    {
        var document = CreateDocument("limit.rocket");
        var layout = new EditorLayoutViewModel();
        layout.OpenOrActivate(document);
        layout.SplitActive(EditorSplitOrientation.Vertical);
        layout.SplitActive(EditorSplitOrientation.Horizontal);
        layout.OpenOrActivate(document);

        Assert.AreEqual(2, layout.Groups.Count);
        Assert.IsTrue(layout.Groups.All(group =>
            group.Views.Count(view => ReferenceEquals(view.Document, document)) <= 1));
    }

    [TestMethod]
    public void CaptureRestore_RoundTripsTwoGroupsAndIndependentViewState()
    {
        var firstDocument = CreateDocument("restore-a.rocket");
        var secondDocument = CreateDocument("restore-b.rocket");
        var documents = new[] { firstDocument, secondDocument };
        var layout = new EditorLayoutViewModel();
        var left = layout.OpenOrActivate(firstDocument);
        left.UpdateCaret(2, 2, 2);
        layout.SplitActive(EditorSplitOrientation.Vertical);
        var right = layout.OpenOrActivate(secondDocument);
        right.UpdateSelection(3, 4);
        right.UpdateScroll(5, 6);
        var state = layout.CaptureState();

        var restored = new EditorLayoutViewModel();
        restored.Restore(state, path => documents.FirstOrDefault(document =>
            string.Equals(document.Path, path, StringComparison.OrdinalIgnoreCase)));

        Assert.AreEqual(2, restored.Groups.Count);
        Assert.AreEqual(EditorSplitOrientation.Vertical, restored.Orientation);
        Assert.AreEqual(2, restored.Groups[0].Views[0].CaretOffset);
        var restoredSecond = restored.Groups[1].Views.Single(view => ReferenceEquals(view.Document, secondDocument));
        Assert.AreEqual(3, restoredSecond.SelectionStart);
        Assert.AreEqual(6d, restoredSecond.VerticalOffset);
    }

    private static DocumentTabViewModel CreateDocument(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), name));
        var id = DocumentId.New();
        var state = new DocumentState(id, path, "fn main() -> Int:\n    return 0\n", Encoding.UTF8.GetByteCount("fn main() -> Int:\n    return 0\n"));
        return new DocumentTabViewModel(new FakeDocumentStore(state), state.Snapshot);
    }

    private sealed class FakeDocumentStore(DocumentState state) : IDocumentStore
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
