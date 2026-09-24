using System.IO;
using System.Text;
using RocketIDE.App.Editor;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Core.Documents;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class LegacySinglePaneEditorIntegrationTests
{
    [TestMethod]
    public void ActiveContext_FollowsLegacyActiveDocumentAndResolvesCommandTarget()
    {
        var viewModel = new MainWindowViewModel(new FakeWorkspaceFileSystem());
        var first = CreateDocument("first.rocket");
        var second = CreateDocument("second.rocket");
        var firstTarget = new FakeEditorCommandTarget();
        var secondTarget = new FakeEditorCommandTarget();
        var targets = new Dictionary<DocumentTabViewModel, IEditorCommandTarget>
        {
            [first] = firstTarget,
            [second] = secondTarget,
        };
        var integration = new LegacySinglePaneEditorIntegration(
            viewModel,
            document => targets.GetValueOrDefault(document),
            (_, _) => Task.FromResult<DocumentTabViewModel?>(null));
        var changes = 0;
        integration.ActiveContextChanged += (_, _) => changes++;

        viewModel.ActiveDocument = first;

        Assert.AreSame(first, integration.ActiveDocument);
        Assert.AreSame(first, integration.ActiveView?.Document);
        Assert.AreSame(firstTarget, integration.ActiveView?.CommandTarget);
        integration.ActiveView?.Focus();
        Assert.AreEqual(1, firstTarget.FocusCount);

        viewModel.ActiveDocument = second;

        Assert.AreSame(second, integration.ActiveDocument);
        Assert.AreSame(secondTarget, integration.ActiveView?.CommandTarget);
        Assert.AreEqual(2, changes);
    }

    [TestMethod]
    public async Task OpenOrRevealAsync_ActivatesDocumentRequestsRangeAndFocusesResolvedEditor()
    {
        var viewModel = new MainWindowViewModel(new FakeWorkspaceFileSystem());
        var document = CreateDocument("target.rocket");
        var target = new FakeEditorCommandTarget();
        var openedPath = string.Empty;
        SourceRange? requestedRange = null;
        document.NavigationRequested += (_, e) => requestedRange = e.Range;
        var integration = new LegacySinglePaneEditorIntegration(
            viewModel,
            candidate => ReferenceEquals(candidate, document) ? target : null,
            (path, _) =>
            {
                openedPath = path;
                return Task.FromResult<DocumentTabViewModel?>(document);
            });
        var range = new SourceRange(2, 3, 4, 5);

        var view = await integration.OpenOrRevealAsync("target.rocket", range);

        Assert.AreEqual("target.rocket", openedPath);
        Assert.AreSame(document, viewModel.ActiveDocument);
        Assert.AreSame(document, integration.ActiveDocument);
        Assert.AreSame(document, view?.Document);
        Assert.AreSame(range, requestedRange);
        Assert.AreSame(range, document.TakePendingNavigation());
        Assert.AreEqual(1, target.FocusCount);
    }

    [TestMethod]
    public async Task OpenOrRevealAsync_CancelledBeforeOpen_DoesNotInvokeLegacyOpenPath()
    {
        var viewModel = new MainWindowViewModel(new FakeWorkspaceFileSystem());
        var openCalls = 0;
        var integration = new LegacySinglePaneEditorIntegration(
            viewModel,
            _ => null,
            (_, _) =>
            {
                openCalls++;
                return Task.FromResult<DocumentTabViewModel?>(null);
            });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            integration.OpenOrRevealAsync("cancelled.rocket", cancellationToken: cancellation.Token));

        Assert.AreEqual(0, openCalls);
    }

    [TestMethod]
    public void EditorDocumentHost_ImplementsSharedCommandTargetContract()
    {
        Assert.IsTrue(typeof(IEditorCommandTarget).IsAssignableFrom(typeof(EditorDocumentHost)));
    }

    private static DocumentTabViewModel CreateDocument(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{fileName}"));
        const string text = "fn main():\n    return 0\n";
        var state = new DocumentState(DocumentId.New(), path, text, Encoding.UTF8.GetByteCount(text));
        return new DocumentTabViewModel(new FakeDocumentStore(state), state.Snapshot);
    }

    private sealed class FakeEditorCommandTarget : IEditorCommandTarget
    {
        public int FocusCount { get; private set; }
        public int CaretLine { get; private set; } = 1;
        public int CaretColumn { get; private set; } = 1;
        public int CaretOffset { get; private set; }
        public int SelectionStart { get; private set; }
        public int SelectionLength { get; private set; }

        public void SetSelection(int startOffset, int length)
        {
            SelectionStart = startOffset;
            SelectionLength = length;
            CaretOffset = startOffset;
        }

        public void FocusEditor() => FocusCount++;
        public void Undo() { }
        public void Redo() { }
        public void SelectAll() { }
        public void ShowFind(bool includeReplace) { }
        public void GoToLine(int line) => CaretLine = line;
    }

    private sealed class FakeWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public Task<IReadOnlyList<WorkspaceEntry>> GetChildrenAsync(string directoryPath, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkspaceEntry>>([]);

        public Task CreateFileAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void CreateDirectory(string path) => throw new NotSupportedException();
        public string Rename(string path, string newName) => throw new NotSupportedException();
        public void DeleteToRecycleBin(string path) => throw new NotSupportedException();
        public string ResolveChildPath(string directoryPath, string leafName) => throw new NotSupportedException();
    }

    private sealed class FakeDocumentStore(DocumentState state) : IDocumentStore
    {
        public IReadOnlyList<DocumentSnapshot> OpenDocuments => [state.Snapshot];

        public Task<DocumentSnapshot> OpenAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(state.Snapshot);

        public DocumentSnapshot UpdateText(DocumentId id, string text) => state.ApplyEdit(text);

        public Task<DocumentSnapshot> ReloadAsync(DocumentId id, CancellationToken cancellationToken) =>
            Task.FromResult(state.Snapshot);

        public Task<DocumentSaveResult> SaveAsync(
            DocumentId id,
            bool overwriteExternalChanges,
            CancellationToken cancellationToken) =>
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
}
