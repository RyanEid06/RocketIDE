using RocketIDE.Core.Documents;

namespace RocketIDE.Core.Tests.Documents;

[TestClass]
public sealed class DocumentStateTests
{
    [TestMethod]
    public void NewDocumentStartsCleanAtVersionZero()
    {
        var state = new DocumentState(new DocumentId(Guid.NewGuid()), @"C:\work\main.rocket", "let x = 1\n", 10);

        var snapshot = state.Snapshot;

        Assert.AreEqual(0, snapshot.Version);
        Assert.IsFalse(snapshot.IsDirty);
        Assert.AreEqual(10L, snapshot.ByteLength);
        Assert.AreEqual("let x = 1\n", snapshot.Text);
    }

    [TestMethod]
    public void ApplyingChangedTextMarksDirtyAndIncrementsVersionMonotonically()
    {
        var state = new DocumentState(new DocumentId(Guid.NewGuid()), @"C:\work\main.rocket", "a", 1);

        var first = state.ApplyEdit("ab");
        var second = state.ApplyEdit("abc");

        Assert.AreEqual(1, first.Version);
        Assert.AreEqual(2L, first.ByteLength);
        Assert.IsTrue(first.IsDirty);
        Assert.AreEqual(2, second.Version);
        Assert.AreEqual(3L, second.ByteLength);
        Assert.IsTrue(second.IsDirty);
    }

    [TestMethod]
    public void ApplyingIdenticalTextDoesNotCreateFakeEditVersion()
    {
        var state = new DocumentState(new DocumentId(Guid.NewGuid()), @"C:\work\main.rocket", "same", 4);

        var snapshot = state.ApplyEdit("same");

        Assert.AreEqual(0, snapshot.Version);
        Assert.IsFalse(snapshot.IsDirty);
    }

    [TestMethod]
    public void EditingBackToSavedTextClearsDirtyWithoutRewindingVersion()
    {
        var state = new DocumentState(new DocumentId(Guid.NewGuid()), @"C:\work\main.rocket", "saved", 5);
        state.ApplyEdit("changed");

        var returned = state.ApplyEdit("saved");

        Assert.AreEqual(2, returned.Version);
        Assert.IsFalse(returned.IsDirty);
        Assert.AreEqual(5L, returned.ByteLength);
    }

    [TestMethod]
    public void ByteLengthTracksCurrentUtf8BufferRatherThanStaleDiskLength()
    {
        var state = new DocumentState(new DocumentId(Guid.NewGuid()), @"C:\work\main.rocket", "a", 1);

        var snapshot = state.ApplyEdit("é");

        Assert.AreEqual(2L, snapshot.ByteLength);
        Assert.IsTrue(snapshot.IsDirty);
    }

    [TestMethod]
    public void MarkSavedClearsDirtyWithoutRewritingEditHistory()
    {
        var state = new DocumentState(new DocumentId(Guid.NewGuid()), @"C:\work\main.rocket", "a", 1);
        state.ApplyEdit("rocket");

        var saved = state.MarkSaved(6);

        Assert.AreEqual(1, saved.Version);
        Assert.IsFalse(saved.IsDirty);
        Assert.AreEqual(6L, saved.ByteLength);
        Assert.AreEqual("rocket", saved.Text);
    }
    [TestMethod]
    public void MarkPersisted_UpdatesSavedBaselineWithoutLyingAboutNewerEditorText()
    {
        var state = new DocumentState(new DocumentId(Guid.NewGuid()), @"C:\work\main.rocket", "disk-v1", 7);
        state.ApplyEdit("saved-in-flight");
        state.ApplyEdit("newer-editor-text");

        var snapshot = state.MarkPersisted("saved-in-flight", 15);

        Assert.AreEqual("newer-editor-text", snapshot.Text);
        Assert.IsTrue(snapshot.IsDirty);
        Assert.AreEqual(System.Text.Encoding.UTF8.GetByteCount("newer-editor-text"), snapshot.ByteLength);
    }

    [TestMethod]
    public void MarkPersisted_ClearsDirtyWhenCurrentTextMatchesPersistedSnapshot()
    {
        var state = new DocumentState(new DocumentId(Guid.NewGuid()), @"C:\work\main.rocket", "old", 3);
        state.ApplyEdit("saved");

        var snapshot = state.MarkPersisted("saved", 5);

        Assert.IsFalse(snapshot.IsDirty);
        Assert.AreEqual(5L, snapshot.ByteLength);
    }

}
