using System.IO;
using System.Text;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Documents;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Tests.Wp03;

[TestClass]
public sealed class SymbolAndFoldingStalenessTests
{
    [TestMethod]
    public async Task DocumentSymbols_RejectResponseAfterDocumentVersionChanges()
    {
        var document = CreateDocument();
        var pending = new TaskCompletionSource<IReadOnlyList<RocketDocumentSymbol>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        long generation = 7;
        var service = new DocumentSymbolService((_, _) => pending.Task, () => generation);
        var request = service.GetAsync(document, CancellationToken.None);

        document.EditorDocument.Insert(document.EditorDocument.TextLength, "// edit\n");
        pending.SetResult([Symbol(document.Path)]);

        Assert.IsNull(await request);
    }

    [TestMethod]
    public async Task FoldingProvider_AllowsConcurrentSameVersionRequestsForSplitViews()
    {
        var document = CreateDocument();
        var first = new TaskCompletionSource<IReadOnlyList<RocketFoldingRange>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<IReadOnlyList<RocketFoldingRange>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new RocketFoldingRangeProvider((_, _) => ++calls == 1 ? first.Task : second.Task, () => 4);
        var firstRequest = provider.GetRangesAsync(document, CancellationToken.None);
        var secondRequest = provider.GetRangesAsync(document, CancellationToken.None);
        second.SetResult([new RocketFoldingRange(0, 0, 1, 0, null)]);
        first.SetResult([new RocketFoldingRange(0, 0, 2, 0, null)]);

        Assert.IsNotNull(await secondRequest);
        Assert.IsNotNull(await firstRequest);
    }

    [TestMethod]
    public async Task FoldingProvider_DoesNotRequestAfterApplicationShutdown()
    {
        var document = CreateDocument();
        using var lifetime = new CancellationTokenSource();
        lifetime.Cancel();
        var requests = 0;
        var provider = new RocketFoldingRangeProvider((_, _) =>
        {
            requests++;
            return Task.FromResult<IReadOnlyList<RocketFoldingRange>?>([]);
        }, () => 4, lifetime.Token);

        Assert.IsNull(await provider.GetRangesAsync(document, CancellationToken.None));
        Assert.AreEqual(0, requests);
    }

    private static RocketDocumentSymbol Symbol(string path)
    {
        var range = new LspRange(new LspPosition(0, 0), new LspPosition(0, 2));
        return new RocketDocumentSymbol("x", null, 13, path, range, range, []);
    }

    private static DocumentTabViewModel CreateDocument()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-stale.rocket"));
        const string text = "fn main():\n    return 0\n";
        var state = new DocumentState(DocumentId.New(), path, text, Encoding.UTF8.GetByteCount(text));
        return new DocumentTabViewModel(new FakeStore(state), state.Snapshot);
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
