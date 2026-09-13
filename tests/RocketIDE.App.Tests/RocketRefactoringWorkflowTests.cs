using RocketIDE.App.Integration;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class RocketRefactoringWorkflowTests
{
    [TestMethod]
    public async Task Rename_PrepareRejectionPreventsPromptAndRenameRequest()
    {
        var service = new FakeFeatures { PrepareResult = null };
        var promptCount = 0;
        var applyCount = 0;
        var workflow = new RocketRenameWorkflow(service);

        var result = await workflow.ExecuteAsync(
            "main.rocket",
            new LspPosition(0, 0),
            _ => { promptCount++; return Task.FromResult<string?>("renamed"); },
            _ => { applyCount++; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.AreEqual(RocketRenameWorkflowStatus.NotRenameable, result.Status);
        Assert.AreEqual(0, promptCount);
        Assert.AreEqual(0, service.RenameRequestCount);
        Assert.AreEqual(0, applyCount);
    }

    [TestMethod]
    public async Task Rename_PrepareSuccessGatesPromptThenAppliesOnlyServerWorkspaceEdit()
    {
        var workspaceEdit = new RocketWorkspaceEdit([]);
        var service = new FakeFeatures
        {
            PrepareResult = new RocketPrepareRenameResult(new LspRange(new LspPosition(0, 0), new LspPosition(0, 4)), "name", false),
            RenameResult = workspaceEdit,
        };
        string? promptInitial = null;
        RocketWorkspaceEdit? applied = null;
        var workflow = new RocketRenameWorkflow(service);

        var result = await workflow.ExecuteAsync(
            "main.rocket",
            new LspPosition(0, 1),
            initial => { promptInitial = initial; return Task.FromResult<string?>("other"); },
            edit => { applied = edit; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.AreEqual(RocketRenameWorkflowStatus.Applied, result.Status);
        Assert.AreEqual("name", promptInitial);
        Assert.AreEqual(1, service.RenameRequestCount);
        Assert.AreSame(workspaceEdit, applied);
    }

    [TestMethod]
    public async Task Rename_CancellationAfterPreparePreventsRenameUiAndRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new FakeFeatures
        {
            PrepareResult = new RocketPrepareRenameResult(null, "name", true),
            OnPrepare = cancellation.Cancel,
        };
        var promptCount = 0;
        var workflow = new RocketRenameWorkflow(service);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => workflow.ExecuteAsync(
            "main.rocket",
            new LspPosition(0, 0),
            _ => { promptCount++; return Task.FromResult<string?>("renamed"); },
            _ => Task.CompletedTask,
            cancellation.Token));

        Assert.AreEqual(0, promptCount);
        Assert.AreEqual(0, service.RenameRequestCount);
    }

    [TestMethod]
    public void CodeActionContext_SelectsOnlyDiagnosticsAtCaretAndUsesTheirRange()
    {
        var caret = new LspRange(new LspPosition(4, 20), new LspPosition(4, 20));
        var matchingRange = new LspRange(new LspPosition(4, 18), new LspPosition(4, 22));
        var otherRange = new LspRange(new LspPosition(8, 0), new LspPosition(8, 4));
        var matching = new RocketCodeActionDiagnostic(matchingRange, 1, "R4002", "rocketc", "unknown name");
        var other = new RocketCodeActionDiagnostic(otherRange, 1, "R4002", "rocketc", "other");

        var context = RocketCodeActionContextBuilder.Build(caret, [matching, other]);

        Assert.AreEqual(1, context.Diagnostics.Count);
        Assert.AreSame(matching, context.Diagnostics[0]);
        Assert.AreEqual(matchingRange, context.Range);
    }

    [TestMethod]
    public void CodeActionContext_LeavesSelectionAndEmptyDiagnosticsWhenNothingOverlaps()
    {
        var caret = new LspRange(new LspPosition(1, 1), new LspPosition(1, 1));
        var otherRange = new LspRange(new LspPosition(8, 0), new LspPosition(8, 4));
        var other = new RocketCodeActionDiagnostic(otherRange, 1, "R4002", "rocketc", "other");

        var context = RocketCodeActionContextBuilder.Build(caret, [other]);

        Assert.AreEqual(0, context.Diagnostics.Count);
        Assert.AreEqual(caret, context.Range);
    }

    [TestMethod]
    public async Task CodeAction_CommandOnlyIsNeverExecutedOrApplied()
    {
        var action = new RocketCodeAction("Do command", "quickfix", null, true);
        var applyCount = 0;

        var result = await RocketCodeActionExecutor.TryApplyAsync(
            action,
            () => true,
            _ => { applyCount++; return Task.CompletedTask; });

        Assert.AreEqual(RocketCodeActionApplyStatus.UnsupportedCommand, result);
        Assert.AreEqual(0, applyCount);
    }


    [TestMethod]
    public async Task CodeAction_StaleSourceVersionPreventsUnversionedEditReuse()
    {
        var action = new RocketCodeAction("Fix name", "quickfix", new RocketWorkspaceEdit([]), false);
        var applyCount = 0;

        var result = await RocketCodeActionExecutor.TryApplyAsync(
            action,
            () => false,
            _ => { applyCount++; return Task.CompletedTask; });

        Assert.AreEqual(RocketCodeActionApplyStatus.Stale, result);
        Assert.AreEqual(0, applyCount);
    }

    [TestMethod]
    public async Task CodeAction_ServerDisabledActionIsNotApplied()
    {
        var action = new RocketCodeAction("Fix name", "quickfix", new RocketWorkspaceEdit([]), false, "Not valid here");
        var applyCount = 0;

        var result = await RocketCodeActionExecutor.TryApplyAsync(
            action,
            () => true,
            _ => { applyCount++; return Task.CompletedTask; });

        Assert.AreEqual(RocketCodeActionApplyStatus.Disabled, result);
        Assert.AreEqual(0, applyCount);
    }

    private sealed class FakeFeatures : IRocketEditorFeatureService
    {
        public RocketPrepareRenameResult? PrepareResult { get; init; }
        public RocketWorkspaceEdit? RenameResult { get; init; }
        public int RenameRequestCount { get; private set; }
        public Action? OnPrepare { get; init; }
        public IReadOnlyList<string> CompletionTriggerCharacters => [];
        public IReadOnlyList<string> SignatureTriggerCharacters => [];
        public IReadOnlyList<string> SignatureRetriggerCharacters => [];
        public Task<RocketPrepareRenameResult?> PrepareRenameAsync(string path, LspPosition position, CancellationToken cancellationToken)
        {
            OnPrepare?.Invoke();
            return Task.FromResult(PrepareResult);
        }
        public Task<RocketWorkspaceEdit?> RequestRenameAsync(string path, LspPosition position, string newName, CancellationToken cancellationToken) { RenameRequestCount++; return Task.FromResult(RenameResult); }
        public Task<RocketCompletionResult?> RequestCompletionAsync(string path, LspPosition position, string? triggerCharacter, CancellationToken cancellationToken) => Task.FromResult<RocketCompletionResult?>(null);
        public Task<RocketHover?> RequestHoverAsync(string path, LspPosition position, CancellationToken cancellationToken) => Task.FromResult<RocketHover?>(null);
        public Task<RocketSignatureHelp?> RequestSignatureHelpAsync(string path, LspPosition position, string? triggerCharacter, bool isRetrigger, CancellationToken cancellationToken) => Task.FromResult<RocketSignatureHelp?>(null);
        public Task<RocketSemanticTokensResult?> RequestSemanticTokensAsync(string path, CancellationToken cancellationToken) => Task.FromResult<RocketSemanticTokensResult?>(null);
        public Task<IReadOnlyList<RocketLocation>?> RequestDefinitionAsync(string path, LspPosition position, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RocketLocation>?>(null);
        public Task<IReadOnlyList<RocketLocation>?> RequestReferencesAsync(string path, LspPosition position, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RocketLocation>?>(null);
        public Task<IReadOnlyList<RocketCodeAction>?> RequestCodeActionsAsync(string path, LspRange range, IReadOnlyList<RocketCodeActionDiagnostic> diagnostics, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RocketCodeAction>?>(null);
        public Task<IReadOnlyList<RocketTextEdit>?> RequestFormattingAsync(string path, int tabSize, bool insertSpaces, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RocketTextEdit>?>(null);
        public void InvalidateSemanticTokens(string path) { }
    }
}
