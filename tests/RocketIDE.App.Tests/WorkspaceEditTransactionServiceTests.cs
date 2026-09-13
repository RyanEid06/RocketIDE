using System.IO;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Infrastructure.Files;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class WorkspaceEditTransactionServiceTests
{
    [TestMethod]
    public async Task ApplyAsync_InvalidEditAnywhereChangesNothing()
    {
        using var temp = new TempDirectory();
        var openPath = Path.Combine(temp.Path, "open.rocket");
        var closedPath = Path.Combine(temp.Path, "closed.rocket");
        var openText = "alpha";
        var disk = new Dictionary<string, ClosedWorkspaceFile>(PathComparer) { [closedPath] = new("beta", false) };
        var writes = new List<string>();
        var service = Service(
            [new WorkspaceEditOpenDocument(openPath, openText, 4, text => openText = text)],
            disk,
            async (path, text, bom, _) => { writes.Add(path); disk[path] = new(text, bom); await Task.CompletedTask; });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(openPath, 4, [Edit(0,0,0,1,"A")]),
            new RocketWorkspaceDocumentEdit(closedPath, null, [Edit(9,0,9,1,"B")]),
        ]);

        await Assert.ThrowsExactlyAsync<WorkspaceEditValidationException>(() => service.ApplyAsync(edit, temp.Path, CancellationToken.None));

        Assert.AreEqual("alpha", openText);
        Assert.AreEqual("beta", disk[closedPath].Text);
        Assert.AreEqual(0, writes.Count);
    }

    [TestMethod]
    public async Task ApplyAsync_InvalidTargetPathIsRejectedBeforeReadOrWrite()
    {
        var reads = 0;
        var writes = 0;
        var service = new WorkspaceEditTransactionService(
            () => [],
            (_, _) => { reads++; return Task.FromResult(new ClosedWorkspaceFile("ignored", false)); },
            _ => true,
            (_, _, _, _) => { writes++; return Task.CompletedTask; });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit("bad\0path.rocket", null, [Edit(0, 0, 0, 0, "x")]),
        ]);

        await Assert.ThrowsExactlyAsync<WorkspaceEditValidationException>(() =>
            service.ApplyAsync(edit, Path.GetTempPath(), CancellationToken.None));

        Assert.AreEqual(0, reads);
        Assert.AreEqual(0, writes);
    }



    [TestMethod]
    public async Task ApplyAsync_AlreadyOpenTargetOutsideWorkspaceUsesOnlyLiveBuffer()
    {
        using var workspace = new TempDirectory();
        using var elsewhere = new TempDirectory();
        var openPath = Path.Combine(elsewhere.Path, "open.rocket");
        var openText = "alpha";
        var probes = 0;
        var service = new WorkspaceEditTransactionService(
            () => [new WorkspaceEditOpenDocument(openPath, openText, 5, text => openText = text)],
            (_, _) => throw new AssertFailedException("Open targets must not be read through the closed-file path."),
            _ => { probes++; return false; },
            (_, _, _, _) => throw new AssertFailedException("Open targets must not be written through the closed-file path."));
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(openPath, 5, [Edit(0, 0, 0, 1, "A")]),
        ]);

        var result = await service.ApplyAsync(edit, workspace.Path, CancellationToken.None);

        Assert.AreEqual(1, result.ChangedDocumentCount);
        Assert.AreEqual("Alpha", openText);
        Assert.AreEqual(0, probes);
    }

    [TestMethod]
    public async Task ApplyAsync_OutOfWorkspaceClosedTargetIsRejectedBeforeFileAccess()
    {
        using var workspace = new TempDirectory();
        using var elsewhere = new TempDirectory();
        var target = Path.Combine(elsewhere.Path, "outside.rocket");
        var reads = 0;
        var probes = 0;
        var writes = 0;
        var service = new WorkspaceEditTransactionService(
            () => [],
            (_, _) => { reads++; return Task.FromResult(new ClosedWorkspaceFile("outside", false)); },
            _ => { probes++; return true; },
            (_, _, _, _) => { writes++; return Task.CompletedTask; });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(target, null, [Edit(0, 0, 0, 1, "X")]),
        ]);

        await Assert.ThrowsExactlyAsync<WorkspaceEditValidationException>(() =>
            service.ApplyAsync(edit, workspace.Path, CancellationToken.None));

        Assert.AreEqual(0, reads);
        Assert.AreEqual(0, probes);
        Assert.AreEqual(0, writes);
    }


    [TestMethod]
    public async Task ApplyAsync_UnwritableClosedTargetIsRejectedBeforeCommit()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "closed.rocket");
        var writes = 0;
        var service = new WorkspaceEditTransactionService(
            () => [],
            (_, _) => Task.FromResult(new ClosedWorkspaceFile("abc", false)),
            _ => false,
            (_, _, _, _) => { writes++; return Task.CompletedTask; });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(target, null, [Edit(0, 0, 0, 1, "X")]),
        ]);

        await Assert.ThrowsExactlyAsync<WorkspaceEditValidationException>(() =>
            service.ApplyAsync(edit, temp.Path, CancellationToken.None));

        Assert.AreEqual(0, writes);
    }

    [TestMethod]
    public async Task ApplyAsync_ClosedTargetBecomesUnwritableAfterValidationRejectsBeforeAnyCommit()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "a.rocket");
        var second = Path.Combine(temp.Path, "b.rocket");
        var disk = new Dictionary<string, ClosedWorkspaceFile>(PathComparer)
        {
            [first] = new("one", false),
            [second] = new("two", false),
        };
        var probes = new Dictionary<string, int>(PathComparer);
        var writes = 0;
        var service = new WorkspaceEditTransactionService(
            () => [],
            (path, _) => Task.FromResult(disk[path]),
            path =>
            {
                probes[path] = probes.GetValueOrDefault(path) + 1;
                return !PathComparer.Equals(path, second) || probes[path] == 1;
            },
            (_, _, _, _) => { writes++; return Task.CompletedTask; });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(first, null, [Edit(0, 0, 0, 1, "O")]),
            new RocketWorkspaceDocumentEdit(second, null, [Edit(0, 0, 0, 1, "T")]),
        ]);

        await Assert.ThrowsExactlyAsync<WorkspaceEditValidationException>(() =>
            service.ApplyAsync(edit, temp.Path, CancellationToken.None));

        Assert.AreEqual(0, writes);
        Assert.AreEqual("one", disk[first].Text);
        Assert.AreEqual("two", disk[second].Text);
    }

    [TestMethod]
    public async Task ApplyAsync_FileSystemServiceUpdatesClosedUtf8FileAndPreservesBom()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "closed.rocket");
        var utf8Bom = new System.Text.UTF8Encoding(true).GetPreamble();
        var body = System.Text.Encoding.UTF8.GetBytes("alpha\r\nbeta\r\n");
        await File.WriteAllBytesAsync(target, utf8Bom.Concat(body).ToArray());
        var service = WorkspaceEditTransactionService.CreateFileSystemService(() => []);
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(target, null, [Edit(0, 0, 0, 1, "A")]),
        ]);

        var result = await service.ApplyAsync(edit, temp.Path, CancellationToken.None);
        var bytes = await File.ReadAllBytesAsync(target);

        Assert.AreEqual(1, result.ChangedDocumentCount);
        Assert.IsTrue(bytes.AsSpan().StartsWith(utf8Bom));
        var text = new System.Text.UTF8Encoding(false, true).GetString(bytes, utf8Bom.Length, bytes.Length - utf8Bom.Length);
        Assert.AreEqual("Alpha\r\nbeta\r\n", text);
    }

    [TestMethod]
    public async Task ApplyAsync_OpenDirtyBufferStaysInMemoryAndClosedFileUsesSafeWriter()
    {
        using var temp = new TempDirectory();
        var openPath = Path.Combine(temp.Path, "open.rocket");
        var closedPath = Path.Combine(temp.Path, "closed.rocket");
        var openText = "alpha";
        var dirty = true;
        var disk = new Dictionary<string, ClosedWorkspaceFile>(PathComparer) { [closedPath] = new("beta", true) };
        var writes = new List<string>();
        var service = Service(
            [new WorkspaceEditOpenDocument(openPath, openText, 3, text => openText = text)],
            disk,
            async (path, text, bom, _) => { writes.Add(path); disk[path] = new(text, bom); await Task.CompletedTask; });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(openPath, 3, [Edit(0,0,0,1,"A")]),
            new RocketWorkspaceDocumentEdit(closedPath, null, [Edit(0,0,0,1,"B")]),
        ]);

        var result = await service.ApplyAsync(edit, temp.Path, CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Alpha", openText);
        Assert.IsTrue(dirty, "Applying a workspace edit must not silently mark/save an open dirty document.");
        CollectionAssert.AreEqual(new[] { closedPath }, writes);
        Assert.AreEqual("Beta", disk[closedPath].Text);
        Assert.IsTrue(disk[closedPath].HasUtf8Bom);
    }


    [TestMethod]
    public async Task ApplyAsync_RealDirtyDocumentBufferRemainsDirtyAndDiskIsNotSilentlySaved()
    {
        using var temp = new TempDirectory();
        var openPath = Path.Combine(temp.Path, "open.rocket");
        await File.WriteAllTextAsync(openPath, "saved");
        var store = new FileDocumentStore();
        var snapshot = await store.OpenAsync(openPath, CancellationToken.None);
        var tab = new DocumentTabViewModel(store, snapshot);
        tab.EditorDocument.Replace(0, tab.EditorDocument.TextLength, "dirty");
        Assert.IsTrue(tab.IsDirty);

        var service = new WorkspaceEditTransactionService(
            () => [new WorkspaceEditOpenDocument(tab.Path, tab.Text, tab.Version, text => tab.EditorDocument.Replace(0, tab.EditorDocument.TextLength, text))],
            (_, _) => throw new AssertFailedException("Open documents must not be re-read as closed files."),
            _ => true,
            (_, _, _, _) => throw new AssertFailedException("Open documents must not be written to disk by WorkspaceEdit commit."));
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(openPath, tab.Version, [Edit(0, 0, 0, 1, "D")]),
        ]);

        await service.ApplyAsync(edit, temp.Path, CancellationToken.None);

        Assert.AreEqual("Dirty", tab.Text);
        Assert.IsTrue(tab.IsDirty);
        Assert.AreEqual("saved", await File.ReadAllTextAsync(openPath));
    }

    [TestMethod]
    public async Task ApplyAsync_OpenDocumentChangedAfterSnapshotRejectsBeforeAnyCommit()
    {
        using var temp = new TempDirectory();
        var openPath = Path.Combine(temp.Path, "open.rocket");
        var closedPath = Path.Combine(temp.Path, "closed.rocket");
        var openText = "alpha";
        var openVersion = 3;
        var applyCount = 0;
        var providerCalls = 0;
        var disk = new Dictionary<string, ClosedWorkspaceFile>(PathComparer) { [closedPath] = new("beta", false) };
        var writes = 0;
        var service = new WorkspaceEditTransactionService(
            () =>
            {
                providerCalls++;
                if (providerCalls > 1)
                {
                    openText = "changed";
                    openVersion = 4;
                }
                return [new WorkspaceEditOpenDocument(openPath, openText, openVersion, _ => applyCount++)];
            },
            (path, _) => Task.FromResult(disk[path]),
            _ => true,
            (_, _, _, _) => { writes++; return Task.CompletedTask; });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(openPath, null, [Edit(0, 0, 0, 1, "A")]),
            new RocketWorkspaceDocumentEdit(closedPath, null, [Edit(0, 0, 0, 1, "B")]),
        ]);

        await Assert.ThrowsExactlyAsync<WorkspaceEditValidationException>(() =>
            service.ApplyAsync(edit, temp.Path, CancellationToken.None));

        Assert.AreEqual(0, writes);
        Assert.AreEqual(0, applyCount);
        Assert.AreEqual("beta", disk[closedPath].Text);
    }

    [TestMethod]
    public async Task ApplyAsync_ClosedFileChangedAfterSnapshotRejectsBeforeAnyCommit()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "a.rocket");
        var second = Path.Combine(temp.Path, "b.rocket");
        var readCounts = new Dictionary<string, int>(PathComparer);
        var disk = new Dictionary<string, ClosedWorkspaceFile>(PathComparer)
        {
            [first] = new("one", false),
            [second] = new("two", false),
        };
        var writes = 0;
        var service = new WorkspaceEditTransactionService(
            () => [],
            (path, _) =>
            {
                readCounts[path] = readCounts.GetValueOrDefault(path) + 1;
                if (PathComparer.Equals(path, second) && readCounts[path] > 1)
                {
                    return Task.FromResult(new ClosedWorkspaceFile("externally changed", false));
                }
                return Task.FromResult(disk[path]);
            },
            _ => true,
            (_, _, _, _) => { writes++; return Task.CompletedTask; });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(first, null, [Edit(0, 0, 0, 1, "O")]),
            new RocketWorkspaceDocumentEdit(second, null, [Edit(0, 0, 0, 1, "T")]),
        ]);

        await Assert.ThrowsExactlyAsync<WorkspaceEditValidationException>(() =>
            service.ApplyAsync(edit, temp.Path, CancellationToken.None));

        Assert.AreEqual(0, writes);
        Assert.AreEqual("one", disk[first].Text);
        Assert.AreEqual("two", disk[second].Text);
    }

    [TestMethod]
    public async Task ApplyAsync_CommitFailureRestoresClosedSnapshotsAndDoesNotTouchOpenBuffer()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "a.rocket");
        var second = Path.Combine(temp.Path, "b.rocket");
        var openPath = Path.Combine(temp.Path, "open.rocket");
        var openText = "open";
        var disk = new Dictionary<string, ClosedWorkspaceFile>(PathComparer)
        {
            [first] = new("one", false),
            [second] = new("two", false),
        };
        var failedOnce = false;
        var service = Service(
            [new WorkspaceEditOpenDocument(openPath, openText, 1, text => openText = text)],
            disk,
            async (path, text, bom, _) =>
            {
                if (PathComparer.Equals(path, second) && !failedOnce)
                {
                    failedOnce = true;
                    throw new IOException("simulated commit failure");
                }
                disk[path] = new(text, bom);
                await Task.CompletedTask;
            });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(first, null, [Edit(0,0,0,1,"O")]),
            new RocketWorkspaceDocumentEdit(second, null, [Edit(0,0,0,1,"T")]),
            new RocketWorkspaceDocumentEdit(openPath, 1, [Edit(0,0,0,1,"O")]),
        ]);

        await Assert.ThrowsExactlyAsync<WorkspaceEditCommitException>(() => service.ApplyAsync(edit, temp.Path, CancellationToken.None));

        Assert.AreEqual("one", disk[first].Text);
        Assert.AreEqual("two", disk[second].Text);
        Assert.AreEqual("open", openText);
    }

    [TestMethod]
    public async Task ApplyAsync_CancellationDuringCommitRestoresEarlierClosedWrite()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "a.rocket");
        var second = Path.Combine(temp.Path, "b.rocket");
        var disk = new Dictionary<string, ClosedWorkspaceFile>(PathComparer)
        {
            [first] = new("one", false),
            [second] = new("two", false),
        };
        using var cancellation = new CancellationTokenSource();
        var service = Service(
            [],
            disk,
            async (path, text, bom, _) =>
            {
                disk[path] = new(text, bom);
                if (PathComparer.Equals(path, first) && text != "one")
                {
                    cancellation.Cancel();
                }
                await Task.CompletedTask;
            });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(first, null, [Edit(0,0,0,1,"O")]),
            new RocketWorkspaceDocumentEdit(second, null, [Edit(0,0,0,1,"T")]),
        ]);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ApplyAsync(edit, temp.Path, cancellation.Token));

        Assert.AreEqual("one", disk[first].Text);
        Assert.AreEqual("two", disk[second].Text);
    }

    [TestMethod]
    public async Task ApplyAsync_StaleOpenVersionRejectsBeforeClosedFileWrite()
    {
        using var temp = new TempDirectory();
        var openPath = Path.Combine(temp.Path, "open.rocket");
        var closedPath = Path.Combine(temp.Path, "closed.rocket");
        var openText = "open";
        var disk = new Dictionary<string, ClosedWorkspaceFile>(PathComparer) { [closedPath] = new("closed", false) };
        var writeCount = 0;
        var service = Service(
            [new WorkspaceEditOpenDocument(openPath, openText, 8, text => openText = text)],
            disk,
            async (path, text, bom, _) => { writeCount++; disk[path] = new(text, bom); await Task.CompletedTask; });
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(openPath, 7, [Edit(0,0,0,1,"O")]),
            new RocketWorkspaceDocumentEdit(closedPath, null, [Edit(0,0,0,1,"C")]),
        ]);

        await Assert.ThrowsExactlyAsync<WorkspaceEditValidationException>(() => service.ApplyAsync(edit, temp.Path, CancellationToken.None));

        Assert.AreEqual(0, writeCount);
        Assert.AreEqual("closed", disk[closedPath].Text);
    }

    private static WorkspaceEditTransactionService Service(
        IReadOnlyList<WorkspaceEditOpenDocument> open,
        IDictionary<string, ClosedWorkspaceFile> disk,
        Func<string,string,bool,CancellationToken,Task> writer) =>
        new(
            () => open,
            (path, _) => Task.FromResult(disk[path]),
            path => open.Any(item => PathComparer.Equals(item.Path, path)) || disk.ContainsKey(path),
            writer);

    private static RocketTextEdit Edit(int sl, int sc, int el, int ec, string text) =>
        new(new LspRange(new LspPosition(sl, sc), new LspPosition(el, ec)), text);

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-apply-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
