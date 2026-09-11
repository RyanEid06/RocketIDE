using System.Text;
using RocketIDE.Core.Documents;
using RocketIDE.Infrastructure.Files;

namespace RocketIDE.Infrastructure.Tests.Files;

[TestClass]
public sealed class FileDocumentStoreTests
{
    private string _tempDirectory = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "RocketIDE.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task OpeningSameNormalizedPathTwiceReturnsSingleDocument()
    {
        var path = Path.Combine(_tempDirectory, "main.rocket");
        await File.WriteAllTextAsync(path, "fn main() -> Int:\n    return 0\n", Encoding.UTF8);
        var store = new FileDocumentStore();

        var first = await store.OpenAsync(path, CancellationToken.None);
        var equivalent = Path.Combine(_tempDirectory, ".", "main.rocket");
        var second = await store.OpenAsync(equivalent, CancellationToken.None);

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, store.OpenDocuments.Count);
    }

    [TestMethod]
    public async Task EditThenSaveTransitionsDirtyToCleanAndPreservesExactText()
    {
        var path = Path.Combine(_tempDirectory, "main.rocket");
        await File.WriteAllTextAsync(path, "before\r\n", new UTF8Encoding(false));
        var store = new FileDocumentStore();
        var opened = await store.OpenAsync(path, CancellationToken.None);

        var edited = store.UpdateText(opened.Id, "after\r\nsecond line\r\n");
        var result = await store.SaveAsync(opened.Id, overwriteExternalChanges: false, CancellationToken.None);

        Assert.IsTrue(edited.IsDirty);
        Assert.AreEqual(DocumentSaveStatus.Saved, result.Status);
        Assert.IsFalse(result.Document.IsDirty);
        Assert.AreEqual("after\r\nsecond line\r\n", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task ExternalChangeConflictsAndNeverSilentlyClearsDirtyState()
    {
        var path = Path.Combine(_tempDirectory, "main.rocket");
        await File.WriteAllTextAsync(path, "disk-v1\n", new UTF8Encoding(false));
        var store = new FileDocumentStore();
        var opened = await store.OpenAsync(path, CancellationToken.None);
        store.UpdateText(opened.Id, "editor-change\n");
        await File.WriteAllTextAsync(path, "external-change\n", new UTF8Encoding(false));

        var result = await store.SaveAsync(opened.Id, overwriteExternalChanges: false, CancellationToken.None);

        Assert.AreEqual(DocumentSaveStatus.Conflict, result.Status);
        Assert.IsTrue(result.Document.IsDirty);
        Assert.AreEqual("external-change\n", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task ExplicitOverwriteAfterConflictWritesEditorBuffer()
    {
        var path = Path.Combine(_tempDirectory, "main.rocket");
        await File.WriteAllTextAsync(path, "disk-v1\n", new UTF8Encoding(false));
        var store = new FileDocumentStore();
        var opened = await store.OpenAsync(path, CancellationToken.None);
        store.UpdateText(opened.Id, "editor-change\n");
        await File.WriteAllTextAsync(path, "external-change\n", new UTF8Encoding(false));

        var result = await store.SaveAsync(opened.Id, overwriteExternalChanges: true, CancellationToken.None);

        Assert.AreEqual(DocumentSaveStatus.Saved, result.Status);
        Assert.IsFalse(result.Document.IsDirty);
        Assert.AreEqual("editor-change\n", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task SaveAsync_EditDuringWriteAndUndoToOldBaselineRemainsDirtyAgainstPersistedDiskText()
    {
        var path = Path.Combine(_tempDirectory, "save-race.rocket");
        await File.WriteAllTextAsync(path, "disk-v1", new UTF8Encoding(false));
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FileDocumentStore(async (target, bytes, cancellationToken) =>
        {
            writeStarted.TrySetResult();
            await releaseWrite.Task.WaitAsync(cancellationToken);
            await File.WriteAllBytesAsync(target, bytes, cancellationToken);
        });
        var opened = await store.OpenAsync(path, CancellationToken.None);
        store.UpdateText(opened.Id, "saved-in-flight");

        var saveTask = store.SaveAsync(opened.Id, overwriteExternalChanges: false, CancellationToken.None);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        store.UpdateText(opened.Id, "disk-v1");
        releaseWrite.TrySetResult();
        var result = await saveTask;

        Assert.AreEqual(DocumentSaveStatus.Saved, result.Status);
        Assert.AreEqual("saved-in-flight", await File.ReadAllTextAsync(path));
        Assert.AreEqual("disk-v1", result.Document.Text);
        Assert.IsTrue(result.Document.IsDirty, "The editor buffer differs from what actually reached disk and must remain dirty.");
    }

    [TestMethod]
    public async Task SaveAsync_SerializesConcurrentSavesForTheSameDocument()
    {
        var path = Path.Combine(_tempDirectory, "concurrent-save.rocket");
        await File.WriteAllTextAsync(path, "disk-v1", new UTF8Encoding(false));
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCount = 0;
        var store = new FileDocumentStore(async (target, bytes, cancellationToken) =>
        {
            var call = Interlocked.Increment(ref writeCount);
            if (call == 1)
            {
                firstWriteStarted.TrySetResult();
                await releaseFirstWrite.Task.WaitAsync(cancellationToken);
            }
            else if (call == 2)
            {
                secondWriteStarted.TrySetResult();
            }

            await File.WriteAllBytesAsync(target, bytes, cancellationToken);
        });
        var opened = await store.OpenAsync(path, CancellationToken.None);
        store.UpdateText(opened.Id, "save-one");

        var firstSave = store.SaveAsync(opened.Id, overwriteExternalChanges: false, CancellationToken.None);
        await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        store.UpdateText(opened.Id, "save-two");
        var secondSave = store.SaveAsync(opened.Id, overwriteExternalChanges: false, CancellationToken.None);

        Assert.IsFalse(secondWriteStarted.Task.IsCompleted, "The second save entered the writer before the first save released its document gate.");
        Assert.AreEqual(1, Volatile.Read(ref writeCount), "The second save must wait until the first atomic replacement completes.");
        releaseFirstWrite.TrySetResult();
        await firstSave;
        await secondWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondResult = await secondSave;

        Assert.AreEqual(2, Volatile.Read(ref writeCount));
        Assert.AreEqual("save-two", await File.ReadAllTextAsync(path));
        Assert.AreEqual(DocumentSaveStatus.Saved, secondResult.Status);
        Assert.IsFalse(secondResult.Document.IsDirty);
    }

    [TestMethod]
    public async Task Utf8BomIsPreservedAcrossSave()
    {
        var path = Path.Combine(_tempDirectory, "bom.rocket");
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(path, "original\n", encoding);
        var store = new FileDocumentStore();
        var opened = await store.OpenAsync(path, CancellationToken.None);
        store.UpdateText(opened.Id, "changed\n");

        await store.SaveAsync(opened.Id, overwriteExternalChanges: false, CancellationToken.None);
        var bytes = await File.ReadAllBytesAsync(path);

        CollectionAssert.AreEqual(encoding.GetPreamble(), bytes[..encoding.GetPreamble().Length]);
    }

    [TestMethod]
    public async Task InvalidUtf8IsRejectedInsteadOfReplacingBytes()
    {
        var path = Path.Combine(_tempDirectory, "invalid.rocket");
        await File.WriteAllBytesAsync(path, [0xC3, 0x28]);
        var store = new FileDocumentStore();

        await Assert.ThrowsExactlyAsync<UnsupportedTextFileException>(
            () => store.OpenAsync(path, CancellationToken.None));
    }

    [TestMethod]
    public async Task InvalidUnicodeBufferFailsSaveWithoutCorruptingDiskFile()
    {
        var path = Path.Combine(_tempDirectory, "invalid-buffer.rocket");
        await File.WriteAllTextAsync(path, "safe\n", new UTF8Encoding(false));
        var store = new FileDocumentStore();
        var opened = await store.OpenAsync(path, CancellationToken.None);
        store.UpdateText(opened.Id, "\uD800");

        await Assert.ThrowsExactlyAsync<UnsupportedTextFileException>(
            () => store.SaveAsync(opened.Id, overwriteExternalChanges: false, CancellationToken.None));

        Assert.AreEqual("safe\n", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task NullByteFileIsRejectedAsBinaryLooking()
    {
        var path = Path.Combine(_tempDirectory, "binary.rocket");
        await File.WriteAllBytesAsync(path, [0x66, 0x6E, 0x00, 0x20]);
        var store = new FileDocumentStore();

        await Assert.ThrowsExactlyAsync<UnsupportedTextFileException>(
            () => store.OpenAsync(path, CancellationToken.None));
    }
}
