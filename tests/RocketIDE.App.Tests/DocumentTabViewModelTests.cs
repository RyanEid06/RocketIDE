using System.IO;
using System.Text;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Documents;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class DocumentTabViewModelTests
{
    [TestMethod]
    public void ApplyWorkspaceReplacement_UpdatesEditorAndMarksBufferDirty()
    {
        var path = Path.Combine(Path.GetTempPath(), "main.rocket");
        var id = DocumentId.New();
        var state = new DocumentState(id, path, "old value", Encoding.UTF8.GetByteCount("old value"));
        var store = new FakeDocumentStore(state);
        var viewModel = new DocumentTabViewModel(store, state.Snapshot);

        viewModel.ApplyWorkspaceReplacement("new value");

        Assert.AreEqual("new value", viewModel.Text);
        Assert.AreEqual("new value", viewModel.EditorDocument.Text);
        Assert.IsTrue(viewModel.IsDirty);
        Assert.AreEqual(1, viewModel.Version);
    }

    [TestMethod]
    public void EditorChange_DisablesFurtherLocalEditingWhenUpdatedSnapshotCrossesLimit()
    {
        var id = DocumentId.New();
        var path = Path.Combine(Path.GetTempPath(), "growing.rocket");
        var initial = new DocumentSnapshot(id, path, "x", 0, false, 1);
        var store = new LimitCrossingDocumentStore(initial);
        var viewModel = new DocumentTabViewModel(store, initial);

        viewModel.EditorDocument.Insert(viewModel.EditorDocument.TextLength, "y");

        Assert.IsFalse(viewModel.AllowLocalEditing);
        Assert.IsFalse(viewModel.AllowFindAndGoto);
        StringAssert.Contains(viewModel.LargeFileReason, "64 MiB");
    }

    [TestMethod]
    public void RecoveryConflict_MustRemainExplicitUntilClearedAfterConfirmedSave()
    {
        var path = Path.Combine(Path.GetTempPath(), "recovered.rocket");
        var id = DocumentId.New();
        var state = new DocumentState(id, path, "disk", Encoding.UTF8.GetByteCount("disk"));
        var store = new FakeDocumentStore(state);
        var viewModel = new DocumentTabViewModel(store, state.Snapshot);

        viewModel.MarkRecoveryConflict("disk changed");

        Assert.IsTrue(viewModel.HasRecoveryConflict);
        Assert.AreEqual("disk changed", viewModel.RecoveryConflictMessage);

        viewModel.ClearRecoveryConflict();

        Assert.IsFalse(viewModel.HasRecoveryConflict);
        Assert.IsNull(viewModel.RecoveryConflictMessage);
    }

    private sealed class LimitCrossingDocumentStore(DocumentSnapshot initial) : IDocumentStore
    {
        private DocumentSnapshot _snapshot = initial;
        public IReadOnlyList<DocumentSnapshot> OpenDocuments => [_snapshot];
        public Task<DocumentSnapshot> OpenAsync(string path, CancellationToken cancellationToken) => Task.FromResult(_snapshot);
        public DocumentSnapshot UpdateText(DocumentId id, string text)
        {
            _snapshot = _snapshot with
            {
                Text = text,
                Version = _snapshot.Version + 1,
                IsDirty = true,
                ByteLength = LargeFilePolicy.MaxEditorBufferBytes + 1,
            };
            return _snapshot;
        }
        public Task<DocumentSnapshot> ReloadAsync(DocumentId id, CancellationToken cancellationToken) => Task.FromResult(_snapshot);
        public Task<DocumentSaveResult> SaveAsync(DocumentId id, bool overwriteExternalChanges, CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentSaveResult(DocumentSaveStatus.NoChanges, _snapshot));
        public Task<IReadOnlyList<DocumentSaveResult>> SaveAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentSaveResult>>([]);
        public bool TryGet(DocumentId id, out DocumentSnapshot? document) { document = _snapshot; return true; }
        public bool Close(DocumentId id) => true;
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
        public bool TryGet(DocumentId id, out DocumentSnapshot? document)
        {
            document = state.Snapshot;
            return true;
        }
        public bool Close(DocumentId id) => true;
    }
    [TestMethod]
    public void DebugMarkersTrackBreakpointsAndCurrentStoppedLine()
    {
        var id = DocumentId.New();
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "debug-markers.rocket"));
        var state = new DocumentState(id, path, "fn main():\n    return 0\n", Encoding.UTF8.GetByteCount("fn main():\n    return 0\n"));
        var store = new FakeDocumentStore(state);
        var tab = new DocumentTabViewModel(store, state.Snapshot);
        var changed = 0;
        tab.DebugMarkersChanged += (_, _) => changed++;

        tab.SetDebugMarkers([1, 2], 2);

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, tab.DebugBreakpointLines.ToArray());
        Assert.AreEqual(2, tab.DebugCurrentLine);
        Assert.AreEqual(1, changed);
    }

}
