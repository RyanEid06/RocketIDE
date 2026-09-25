using System.IO;
using System.Text;
using ICSharpCode.AvalonEdit;
using RocketIDE.App.Editor;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Documents;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class RocketFoldingControllerTests
{
    [TestMethod]
    public void ValidRangesCreateViewLocalFoldsAndInvalidRangesAreIgnored()
    {
        RunSta(() =>
        {
            var document = CreateDocument("one\n  two\n  three\nfour");
            var provider = new ImmediateProvider([
                new EditorFoldingRange(0, 2),
                new EditorFoldingRange(-1, 2),
                new EditorFoldingRange(3, 3),
                new EditorFoldingRange(0, 99),
            ]);
            var editor = new TextEditor { Document = document.EditorDocument };
            using var controller = new RocketFoldingController(editor, provider);
            controller.Attach(document);
            controller.RefreshAsync().GetAwaiter().GetResult();

            controller.RestoreCollapsedState([new EditorFoldingRange(0, 2), new EditorFoldingRange(0, 99)]);
            CollectionAssert.AreEqual(
                new[] { new EditorFoldingRange(0, 2) },
                controller.CaptureCollapsedState().ToArray());
        });
    }

    [TestMethod]
    public void TwoViewsOfSameDocumentKeepIndependentCollapsedState()
    {
        RunSta(() =>
        {
            var document = CreateDocument("one\n  two\n  three\nfour");
            var provider = new ImmediateProvider([new EditorFoldingRange(0, 2)]);
            var firstEditor = new TextEditor { Document = document.EditorDocument };
            var secondEditor = new TextEditor { Document = document.EditorDocument };
            using var first = new RocketFoldingController(firstEditor, provider);
            using var second = new RocketFoldingController(secondEditor, provider);
            first.Attach(document);
            second.Attach(document);
            first.RefreshAsync().GetAwaiter().GetResult();
            second.RefreshAsync().GetAwaiter().GetResult();

            first.RestoreCollapsedState([new EditorFoldingRange(0, 2)]);

            Assert.AreEqual(1, first.CaptureCollapsedState().Count);
            Assert.AreEqual(0, second.CaptureCollapsedState().Count);
            Assert.AreSame(firstEditor.Document, secondEditor.Document);

            first.Detach();
            Assert.AreEqual(0, first.CaptureCollapsedState().Count);
            Assert.AreEqual(0, second.CaptureCollapsedState().Count);
            second.RestoreCollapsedState([new EditorFoldingRange(0, 2)]);
            Assert.AreEqual(1, second.CaptureCollapsedState().Count);
        });
    }

    [TestMethod]
    public void AuthoritativeAdapterMapsCurrentSnapshotAndIgnoresUnsupportedResult()
    {
        RunSta(() =>
        {
            var document = CreateDocument("one\n  two\n  three\nfour");
            var provider = new SnapshotProvider(new RocketFoldingSnapshot(
                document.Path, document.Version, 3,
                [new RocketFoldingRange(0, 0, 2, 0, null)]));
            var adapter = new RocketFoldingRangeAdapter(provider, () => document);
            var editor = new TextEditor { Document = document.EditorDocument };
            using var controller = new RocketFoldingController(editor, adapter);
            controller.Attach(document);
            controller.RefreshAsync().GetAwaiter().GetResult();
            controller.RestoreCollapsedState([new EditorFoldingRange(0, 2)]);
            Assert.AreEqual(1, controller.CaptureCollapsedState().Count);

            provider.Snapshot = null;
            controller.RefreshAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, controller.CaptureCollapsedState().Count);
        });
    }

    [TestMethod]
    public void Detach_AllowsSameViewToBindDifferentLogicalDocumentSafely()
    {
        RunSta(() =>
        {
            var firstDocument = CreateDocument("one\n  two\n  three");
            var secondDocument = CreateDocument("alpha\n  beta\n  gamma", "second-folding.rocket");
            var provider = new ImmediateProvider([new EditorFoldingRange(0, 2)]);
            var editor = new TextEditor { Document = firstDocument.EditorDocument };
            using var controller = new RocketFoldingController(editor, provider);

            controller.Attach(firstDocument);
            controller.RefreshAsync().GetAwaiter().GetResult();
            controller.Detach();
            editor.Document = secondDocument.EditorDocument;
            controller.Attach(secondDocument);
            controller.RefreshAsync().GetAwaiter().GetResult();
            controller.RestoreCollapsedState([new EditorFoldingRange(0, 2)]);

            Assert.AreEqual(1, controller.CaptureCollapsedState().Count);
            Assert.AreSame(secondDocument.EditorDocument, editor.Document);
        });
    }

    [TestMethod]
    public void StaleRangeResponseCannotCommitAfterDocumentVersionChanges()
    {
        RunSta(() =>
        {
            var document = CreateDocument("one\n  two\n  three\nfour");
            var provider = new VersionAwareDelayedProvider();
            var editor = new TextEditor { Document = document.EditorDocument };
            using var controller = new RocketFoldingController(editor, provider);
            controller.Attach(document);
            var refresh = controller.RefreshAsync();
            provider.WaitUntilRequested();

            document.EditorDocument.Insert(0, "changed\n");
            provider.Release();
            try
            {
                refresh.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // The version change supersedes the in-flight request. Cancellation is expected.
            }
            Thread.Sleep(220);

            controller.RestoreCollapsedState([new EditorFoldingRange(0, 2)]);
            Assert.AreEqual(0, controller.CaptureCollapsedState().Count);
        });
    }

    private static DocumentTabViewModel CreateDocument(string text, string fileName = "folding.rocket")
    {
        var id = DocumentId.New();
        var path = Path.GetFullPath(fileName);
        var state = new DocumentState(id, path, text, Encoding.UTF8.GetByteCount(text));
        return new DocumentTabViewModel(new FakeDocumentStore(state), state.Snapshot);
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
        thread.Join();
        if (failure is not null)
        {
            Assert.Fail(failure.ToString());
        }
    }

    private sealed class ImmediateProvider(IReadOnlyList<EditorFoldingRange> ranges) : IEditorFoldingRangeProvider
    {
        public Task<IReadOnlyList<EditorFoldingRange>?> RequestRangesAsync(string path, int documentVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<EditorFoldingRange>?>(ranges);
        }
    }

    private sealed class SnapshotProvider(RocketFoldingSnapshot? snapshot) : IRocketFoldingRangeProvider
    {
        public event EventHandler? SessionChanged
        {
            add { }
            remove { }
        }
        public RocketFoldingSnapshot? Snapshot { get; set; } = snapshot;
        public Task<RocketFoldingSnapshot?> GetRangesAsync(DocumentTabViewModel document, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class VersionAwareDelayedProvider : IEditorFoldingRangeProvider
    {
        private readonly ManualResetEventSlim _requested = new();
        private readonly ManualResetEventSlim _release = new();

        public Task<IReadOnlyList<EditorFoldingRange>?> RequestRangesAsync(string path, int documentVersion, CancellationToken cancellationToken)
        {
            _requested.Set();
            if (documentVersion > 0)
            {
                return Task.FromResult<IReadOnlyList<EditorFoldingRange>?>([]);
            }
            return Task.Run<IReadOnlyList<EditorFoldingRange>?>(() =>
            {
                WaitHandle.WaitAny([_release.WaitHandle, cancellationToken.WaitHandle]);
                cancellationToken.ThrowIfCancellationRequested();
                return [new EditorFoldingRange(0, 2)];
            }, cancellationToken);
        }

        public void WaitUntilRequested() => Assert.IsTrue(_requested.Wait(TimeSpan.FromSeconds(2)));
        public void Release() => _release.Set();
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
}
