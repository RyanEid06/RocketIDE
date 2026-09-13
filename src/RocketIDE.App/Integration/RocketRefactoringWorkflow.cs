using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Integration;

public enum RocketRenameWorkflowStatus
{
    Applied,
    NotRenameable,
    Cancelled,
    NoEdit,
}

public sealed record RocketRenameWorkflowResult(RocketRenameWorkflowStatus Status);

public sealed class RocketRenameWorkflow(IRocketEditorFeatureService featureService)
{
    private readonly IRocketEditorFeatureService _featureService = featureService ?? throw new ArgumentNullException(nameof(featureService));

    public async Task<RocketRenameWorkflowResult> ExecuteAsync(
        string path,
        LspPosition position,
        Func<string?, Task<string?>> promptForNameAsync,
        Func<RocketWorkspaceEdit, Task> applyWorkspaceEditAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(promptForNameAsync);
        ArgumentNullException.ThrowIfNull(applyWorkspaceEditAsync);

        var prepared = await _featureService.PrepareRenameAsync(path, position, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (prepared is null)
        {
            return new RocketRenameWorkflowResult(RocketRenameWorkflowStatus.NotRenameable);
        }

        var newName = await promptForNameAsync(prepared.Placeholder);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return new RocketRenameWorkflowResult(RocketRenameWorkflowStatus.Cancelled);
        }

        var edit = await _featureService.RequestRenameAsync(path, position, newName.Trim(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (edit is null)
        {
            return new RocketRenameWorkflowResult(RocketRenameWorkflowStatus.NoEdit);
        }

        await applyWorkspaceEditAsync(edit);
        return new RocketRenameWorkflowResult(RocketRenameWorkflowStatus.Applied);
    }
}

public sealed record RocketCodeActionRequestContext(
    LspRange Range,
    IReadOnlyList<RocketCodeActionDiagnostic> Diagnostics);

public static class RocketCodeActionContextBuilder
{
    public static RocketCodeActionRequestContext Build(
        LspRange requestedRange,
        IReadOnlyList<RocketCodeActionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(requestedRange);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var relevant = diagnostics.Where(diagnostic => Overlaps(requestedRange, diagnostic.Range)).ToArray();
        if (relevant.Length == 0)
        {
            return new RocketCodeActionRequestContext(requestedRange, []);
        }

        var start = relevant.Select(item => item.Range.Start).Aggregate(Min);
        var end = relevant.Select(item => item.Range.End).Aggregate(Max);
        return new RocketCodeActionRequestContext(new LspRange(start, end), relevant);
    }

    private static bool Overlaps(LspRange requested, LspRange diagnostic)
    {
        if (Compare(requested.Start, requested.End) == 0)
        {
            return Compare(diagnostic.Start, requested.Start) <= 0 && Compare(requested.Start, diagnostic.End) <= 0;
        }

        return Compare(diagnostic.Start, requested.End) < 0 && Compare(requested.Start, diagnostic.End) < 0;
    }

    private static LspPosition Min(LspPosition left, LspPosition right) => Compare(left, right) <= 0 ? left : right;
    private static LspPosition Max(LspPosition left, LspPosition right) => Compare(left, right) >= 0 ? left : right;

    private static int Compare(LspPosition left, LspPosition right)
    {
        var line = left.Line.CompareTo(right.Line);
        return line != 0 ? line : left.Character.CompareTo(right.Character);
    }
}

public enum RocketCodeActionApplyStatus
{
    Applied,
    NoEdit,
    UnsupportedCommand,
    Disabled,
    Stale,
}

public static class RocketCodeActionExecutor
{
    public static async Task<RocketCodeActionApplyStatus> TryApplyAsync(
        RocketCodeAction action,
        Func<bool> isStillCurrent,
        Func<RocketWorkspaceEdit, Task> applyWorkspaceEditAsync)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(isStillCurrent);
        ArgumentNullException.ThrowIfNull(applyWorkspaceEditAsync);
        if (action.HasUnsupportedCommand)
        {
            return RocketCodeActionApplyStatus.UnsupportedCommand;
        }
        if (!string.IsNullOrWhiteSpace(action.DisabledReason))
        {
            return RocketCodeActionApplyStatus.Disabled;
        }
        if (action.Edit is null)
        {
            return RocketCodeActionApplyStatus.NoEdit;
        }
        if (!isStillCurrent())
        {
            return RocketCodeActionApplyStatus.Stale;
        }
        await applyWorkspaceEditAsync(action.Edit);
        return RocketCodeActionApplyStatus.Applied;
    }
}
