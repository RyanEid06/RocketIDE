using System.IO;
using System.Text;
using RocketIDE.App.Editor;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Core.Documents;

namespace RocketIDE.App.Tests.Wp03;

[TestClass]
public sealed class NavigationHistoryTests
{
    [TestMethod]
    public async Task BackForwardAndNewBranchUseLogicalLocationsOnly()
    {
        var first = CreateDocument("first.rocket");
        var second = CreateDocument("second.rocket");
        var target = new FakeTarget { CaretLineValue = 3, CaretColumnValue = 5 };
        var context = new FakeEditorContext(new FakeView(first, target));
        var navigation = new FakeNavigation(context, new Dictionary<string, DocumentTabViewModel>(StringComparer.OrdinalIgnoreCase)
        {
            [first.Path] = first,
            [second.Path] = second,
        });
        var history = new NavigationHistoryService(context, navigation, 3);
        var destination = new SourceRange(8, 1, 8, 4);

        Assert.IsTrue(await history.NavigateAsync(second.Path, destination, CancellationToken.None));
        Assert.IsTrue(history.CanGoBack);
        target.CaretLineValue = 9;
        target.CaretColumnValue = 2;
        Assert.IsTrue(await history.GoBackAsync(CancellationToken.None));
        Assert.IsTrue(history.CanGoForward);
        Assert.IsTrue(await history.GoForwardAsync(CancellationToken.None));
    }

    private static DocumentTabViewModel CreateDocument(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{name}"));
        const string text = "fn main():\n    return 0\n";
        var state = new DocumentState(DocumentId.New(), path, text, Encoding.UTF8.GetByteCount(text));
        return new DocumentTabViewModel(new FakeStore(state), state.Snapshot);
    }

    private sealed class FakeEditorContext(FakeView active) : IEditorContext
    {
        public IEditorViewContext? ActiveView { get; private set; } = active;
        public DocumentTabViewModel? ActiveDocument => ActiveView?.Document;
        public event EventHandler? ActiveContextChanged;
        public void Set(FakeView view) { ActiveView = view; ActiveContextChanged?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FakeNavigation(FakeEditorContext context, IReadOnlyDictionary<string, DocumentTabViewModel> docs) : IEditorNavigation
    {
        public List<(string Path, SourceRange? Range)> Calls { get; } = [];
        public Task<IEditorViewContext?> OpenOrRevealAsync(string path, SourceRange? range = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((path, range));
            if (!docs.TryGetValue(Path.GetFullPath(path), out var doc)) return Task.FromResult<IEditorViewContext?>(null);
            var view = new FakeView(doc, new FakeTarget());
            context.Set(view);
            return Task.FromResult<IEditorViewContext?>(view);
        }
    }

    private sealed class FakeView(DocumentTabViewModel document, IEditorCommandTarget target) : IEditorViewContext
    {
        public DocumentTabViewModel Document { get; } = document;
        public IEditorCommandTarget? CommandTarget { get; } = target;
        public void Focus() => CommandTarget?.FocusEditor();
    }

    private sealed class FakeTarget : IEditorCommandTarget
    {
        public int CaretLineValue { get; set; } = 1;
        public int CaretColumnValue { get; set; } = 1;
        public int CaretLine => CaretLineValue;
        public int CaretColumn => CaretColumnValue;
        public int CaretOffset => 0;
        public int SelectionStart => 0;
        public int SelectionLength => 0;
        public void SetSelection(int startOffset, int length) { }
        public void FocusEditor() { }
        public void Undo() { }
        public void Redo() { }
        public void SelectAll() { }
        public void ShowFind(bool includeReplace) { }
        public void GoToLine(int line) => CaretLineValue = line;
    }

    private sealed class FakeStore(DocumentState state) : IDocumentStore
    {
        public IReadOnlyList<DocumentSnapshot> OpenDocuments => [state.Snapshot];
        public Task<DocumentSnapshot> OpenAsync(string path, CancellationToken cancellationToken) => Task.FromResult(state.Snapshot);
        public DocumentSnapshot UpdateText(DocumentId id, string text) => state.ApplyEdit(text);
        public Task<DocumentSnapshot> ReloadAsync(DocumentId id, CancellationToken cancellationToken) => Task.FromResult(state.Snapshot);
        public Task<DocumentSaveResult> SaveAsync(DocumentId id, bool overwriteExternalChanges, CancellationToken cancellationToken) => Task.FromResult(new DocumentSaveResult(DocumentSaveStatus.NoChanges, state.Snapshot));
        public Task<IReadOnlyList<DocumentSaveResult>> SaveAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DocumentSaveResult>>([]);
        public bool TryGet(DocumentId id, out DocumentSnapshot? document) { document = state.Snapshot; return true; }
        public bool Close(DocumentId id) => true;
    }
}
