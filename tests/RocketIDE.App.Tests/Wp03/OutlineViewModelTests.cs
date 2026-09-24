using System.IO;
using System.Text;
using RocketIDE.App.Editor;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Core.Documents;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Tests.Wp03;

[TestClass]
public sealed class OutlineViewModelTests
{
    [TestMethod]
    public async Task Refresh_FollowsActiveDocumentAndNeverKeepsPreviousSymbols()
    {
        var first = CreateDocument("first.rocket");
        var second = CreateDocument("second.rocket");
        var context = new FakeContext(new FakeView(first));
        var service = new DocumentSymbolService((path, _) => Task.FromResult<IReadOnlyList<RocketDocumentSymbol>?>([Symbol(path)]), () => 1);
        var navigation = new NavigationHistoryService(context, new FakeNavigation(context), 10);
        using var outline = new OutlineViewModel(context, service, navigation, CancellationToken.None);

        await outline.RefreshAsync();
        Assert.AreEqual(first.Path, outline.Items.Single().Symbol.Path);

        context.Set(new FakeView(second));
        await outline.RefreshAsync();
        Assert.AreEqual(second.Path, outline.Items.Single().Symbol.Path);
    }

    private static RocketDocumentSymbol Symbol(string path)
    {
        var range = new LspRange(new LspPosition(0, 0), new LspPosition(0, 2));
        return new RocketDocumentSymbol(Path.GetFileNameWithoutExtension(path), null, 12, path, range, range, []);
    }

    private static DocumentTabViewModel CreateDocument(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{name}"));
        const string text = "fn main():\n    return 0\n";
        var state = new DocumentState(DocumentId.New(), path, text, Encoding.UTF8.GetByteCount(text));
        return new DocumentTabViewModel(new FakeStore(state), state.Snapshot);
    }

    private sealed class FakeContext(FakeView active) : IEditorContext
    {
        public IEditorViewContext? ActiveView { get; private set; } = active;
        public DocumentTabViewModel? ActiveDocument => ActiveView?.Document;
        public event EventHandler? ActiveContextChanged;
        public void Set(FakeView view) { ActiveView = view; ActiveContextChanged?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FakeView(DocumentTabViewModel document) : IEditorViewContext
    {
        public DocumentTabViewModel Document { get; } = document;
        public IEditorCommandTarget? CommandTarget { get; } = new FakeTarget();
        public void Focus() { }
    }

    private sealed class FakeNavigation(FakeContext context) : IEditorNavigation
    {
        public Task<IEditorViewContext?> OpenOrRevealAsync(string path, SourceRange? range = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(context.ActiveView);
    }

    private sealed class FakeTarget : IEditorCommandTarget
    {
        public int CaretLine => 1;
        public int CaretColumn => 1;
        public int CaretOffset => 0;
        public int SelectionStart => 0;
        public int SelectionLength => 0;
        public void SetSelection(int startOffset, int length) { }
        public void FocusEditor() { }
        public void Undo() { }
        public void Redo() { }
        public void SelectAll() { }
        public void ShowFind(bool includeReplace) { }
        public void GoToLine(int line) { }
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
