using System.IO;
using System.Text;
using System.Windows;
using RocketIDE.App.Editor;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Documents;
using RocketIDE.Core.Recovery;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class EditorDocumentHostStateTests
{
    [TestMethod]
    public void BindingAndRebindingHostPreserveViewCaretSelectionAndScroll()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/RocketIDE;component/Themes/DarkTheme.xaml", UriKind.Relative),
                });
                var text = string.Join("\n", Enumerable.Repeat(new string('a', 160), 100));
                var state = new DocumentState(DocumentId.New(), Path.GetFullPath("host-state.txt"), text, Encoding.UTF8.GetByteCount(text));
                var document = new DocumentTabViewModel(new FakeDocumentStore(state), state.Snapshot);
                using var first = new EditorViewViewModel(document);
                using var second = new EditorViewViewModel(document);
                first.RestoreState(new EditorViewState { CaretOffset = 9, SelectionStart = 3, SelectionLength = 6, HorizontalOffset = 12, VerticalOffset = 20 });
                second.RestoreState(new EditorViewState { CaretOffset = 20, SelectionStart = 17, SelectionLength = 3, HorizontalOffset = 6, VerticalOffset = 40 });

                var host = new EditorDocumentHost { Width = 300, Height = 140 };
                LayoutHost(host);
                host.DataContext = first;
                LayoutHost(host);
                AssertState(host, first, 9, 3, 6);
                Assert.AreEqual(12d, first.HorizontalOffset);
                Assert.AreEqual(20d, first.VerticalOffset);
                host.DataContext = second;
                LayoutHost(host);
                AssertState(host, second, 20, 17, 3);
                Assert.AreEqual(6d, second.HorizontalOffset);
                Assert.AreEqual(40d, second.VerticalOffset);
                host.DataContext = first;
                LayoutHost(host);
                AssertState(host, first, 9, 3, 6);
                Assert.AreEqual(12d, first.HorizontalOffset);
                Assert.AreEqual(20d, first.VerticalOffset);
                host.DataContext = null;
                first.RestoreState(new EditorViewState { CaretOffset = 3, SelectionStart = 3, SelectionLength = 6 });
                host.DataContext = first;
                AssertState(host, first, 3, 3, 6);
                host.DataContext = null;
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "Host state test did not complete.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private static void LayoutHost(EditorDocumentHost host)
    {
        host.Measure(new Size(300, 140));
        host.Arrange(new Rect(0, 0, 300, 140));
        host.UpdateLayout();
    }

    private static void AssertState(EditorDocumentHost host, EditorViewViewModel view, int caret, int start, int length)
    {
        Assert.AreEqual(caret, host.CaretOffset, "Host caret");
        Assert.AreEqual(start, host.SelectionStart, "Host selection start");
        Assert.AreEqual(length, host.SelectionLength, "Host selection length");
        Assert.AreEqual(caret, view.CaretOffset, "Stored caret");
        Assert.AreEqual(start, view.SelectionStart, "Stored selection start");
        Assert.AreEqual(length, view.SelectionLength, "Stored selection length");
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
