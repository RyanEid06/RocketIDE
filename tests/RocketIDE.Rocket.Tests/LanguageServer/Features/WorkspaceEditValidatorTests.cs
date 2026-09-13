using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class WorkspaceEditValidatorTests
{
    [TestMethod]
    public void ValidateAndCompute_RejectsInvalidEditAnywhereWithoutProducingPlan()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "a.rocket");
        var second = Path.Combine(temp.Path, "b.rocket");
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(first, null, [Edit(0, 0, 0, 1, "A")]),
            new RocketWorkspaceDocumentEdit(second, null, [Edit(99, 0, 99, 1, "B")]),
        ]);
        var context = Context(temp.Path,
            Snapshot(first, "alpha", isOpen: false),
            Snapshot(second, "beta", isOpen: false));

        Assert.ThrowsExactly<WorkspaceEditValidationException>(() => WorkspaceEditValidator.ValidateAndCompute(edit, context));
    }

    [TestMethod]
    public void ValidateAndCompute_RejectsMoreThan1024EditsBeforeMutation()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var edits = Enumerable.Range(0, 1025).Select(_ => Edit(0, 0, 0, 0, "x")).ToArray();
        var workspaceEdit = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(path, null, edits)]);

        var exception = Assert.ThrowsExactly<WorkspaceEditValidationException>(() =>
            WorkspaceEditValidator.ValidateAndCompute(workspaceEdit, Context(temp.Path, Snapshot(path, "", false))));
        StringAssert.Contains(exception.Message, "1,024");
    }

    [TestMethod]
    public void ValidateAndCompute_RejectsOverlappingEditsAndInvalidUtf16Range()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var overlap = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(path, null, [
            Edit(0, 0, 0, 3, "one"), Edit(0, 2, 0, 4, "two")])]);
        Assert.ThrowsExactly<WorkspaceEditValidationException>(() =>
            WorkspaceEditValidator.ValidateAndCompute(overlap, Context(temp.Path, Snapshot(path, "abcdef", false))));

        var surrogate = "a😀b";
        var splitSurrogate = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(path, null, [Edit(0, 2, 0, 3, "x")])]);
        Assert.ThrowsExactly<WorkspaceEditValidationException>(() =>
            WorkspaceEditValidator.ValidateAndCompute(splitSurrogate, Context(temp.Path, Snapshot(path, surrogate, false))));
    }

    [TestMethod]
    public void ValidateAndCompute_RejectsOutOfWorkspaceClosedFileButAllowsAlreadyOpenFile()
    {
        using var workspace = new TempDirectory();
        using var elsewhere = new TempDirectory();
        var path = Path.Combine(elsewhere.Path, "open.rocket");
        var edit = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(path, null, [Edit(0, 0, 0, 1, "X")])]);

        Assert.ThrowsExactly<WorkspaceEditValidationException>(() =>
            WorkspaceEditValidator.ValidateAndCompute(edit, Context(workspace.Path, Snapshot(path, "abc", false))));

        var plan = WorkspaceEditValidator.ValidateAndCompute(
            edit, Context(workspace.Path, new WorkspaceEditDocumentSnapshot(path, "abc", 3, true, false)));
        Assert.AreEqual("Xbc", plan.Documents.Single().ResultText);
    }


    [TestMethod]
    public void ValidateAndCompute_NormalizesTraversalAndRejectsClosedTargetOutsideWorkspace()
    {
        using var workspace = new TempDirectory();
        var parent = Directory.GetParent(workspace.Path)!.FullName;
        var outside = Path.Combine(parent, $"outside-{Guid.NewGuid():N}.rocket");
        var traversal = Path.Combine(workspace.Path, "nested", "..", "..", Path.GetFileName(outside));
        var edit = new RocketWorkspaceEdit([
            new RocketWorkspaceDocumentEdit(traversal, null, [Edit(0, 0, 0, 1, "X")]),
        ]);
        var context = Context(workspace.Path, new WorkspaceEditDocumentSnapshot(outside, "abc", null, false, true));

        Assert.ThrowsExactly<WorkspaceEditValidationException>(() =>
            WorkspaceEditValidator.ValidateAndCompute(edit, context));
    }

    [TestMethod]
    public void ValidateAndCompute_RejectsUnwritableClosedTargetAndStaleSuppliedOpenVersion()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var unversioned = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(path, null, [Edit(0, 0, 0, 1, "X")])]);
        var versioned = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(path, 7, [Edit(0, 0, 0, 1, "X")])]);

        Assert.ThrowsExactly<WorkspaceEditValidationException>(() => WorkspaceEditValidator.ValidateAndCompute(
            unversioned, Context(temp.Path, new WorkspaceEditDocumentSnapshot(path, "abc", null, false, false))));
        Assert.ThrowsExactly<WorkspaceEditValidationException>(() => WorkspaceEditValidator.ValidateAndCompute(
            versioned, Context(temp.Path, new WorkspaceEditDocumentSnapshot(path, "abc", 8, true, true))));
    }

    [TestMethod]
    public void ValidateAndCompute_RejectsInvalidUnicodeResultBeforeCommitPlanExists()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var edit = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(path, null, [Edit(0, 0, 0, 1, "\uD800")])]);

        var exception = Assert.ThrowsExactly<WorkspaceEditValidationException>(() =>
            WorkspaceEditValidator.ValidateAndCompute(edit, Context(temp.Path, Snapshot(path, "abc", false))));

        StringAssert.Contains(exception.Message, "Unicode");
    }

    [TestMethod]
    public void ValidateAndCompute_DoesNotInventVersionRequirementWhenServerOmitsVersion()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "main.rocket");
        var edit = new RocketWorkspaceEdit([new RocketWorkspaceDocumentEdit(path, null, [Edit(0, 0, 0, 1, "X")])]);

        var plan = WorkspaceEditValidator.ValidateAndCompute(
            edit, Context(temp.Path, new WorkspaceEditDocumentSnapshot(path, "abc", 99, true, true)));

        Assert.AreEqual("Xbc", plan.Documents.Single().ResultText);
    }

    private static RocketTextEdit Edit(int sl, int sc, int el, int ec, string text) =>
        new(new LspRange(new LspPosition(sl, sc), new LspPosition(el, ec)), text);

    private static WorkspaceEditDocumentSnapshot Snapshot(string path, string text, bool isOpen) =>
        new(path, text, isOpen ? 3 : null, isOpen, true);

    private static WorkspaceEditValidationContext Context(string root, params WorkspaceEditDocumentSnapshot[] documents) =>
        new(root, documents);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-wp09-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
