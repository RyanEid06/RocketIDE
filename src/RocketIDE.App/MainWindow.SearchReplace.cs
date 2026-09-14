using RocketIDE.App.Integration;
using RocketIDE.Core.Search;
using RocketIDE.Infrastructure.Files;

namespace RocketIDE.App;

public partial class MainWindow
{
    private async Task<ReplaceApplyResult> ApplyWorkspaceReplaceAsync(
        ReplacePreview preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var openBuffers = new List<PreparedOpenBuffer>();
        var closedFiles = new List<ReplacePreviewFile>();

        foreach (var file in preview.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tab = FindOpenDocument(file.FilePath);
            if (tab is null)
            {
                if (file.IsInMemory)
                {
                    throw new InvalidOperationException(
                        $"'{file.FilePath}' was open when the replace preview was created but is no longer open. Create a new preview before applying.");
                }

                closedFiles.Add(file);
                continue;
            }

            if (!file.IsInMemory)
            {
                throw new InvalidOperationException(
                    $"'{file.FilePath}' is open in the editor but the replace preview was created from disk. Create a new preview before applying.");
            }
            if (!tab.AllowLocalEditing)
            {
                throw new InvalidOperationException(
                    $"'{file.FilePath}' exceeds the local-editing safety limit and cannot participate in Replace in Files.");
            }

            var fingerprint = WorkspaceReplaceText.ComputeFingerprint(tab.Text);
            if (!string.Equals(fingerprint, file.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The open editor buffer changed after the replace preview was created: '{file.FilePath}'.");
            }

            var replacementText = WorkspaceReplaceText.Apply(tab.Text, file.Matches, preview.Replacement);
            openBuffers.Add(new PreparedOpenBuffer(tab, file.Fingerprint, replacementText, file.Matches.Count));
        }

        // Fully prepare closed-file writes before touching either disk files or editor buffers.
        // This validates fingerprints, UTF-8 decoding and every previewed range up front.
        WorkspaceReplaceFileTransaction? closedTransaction = null;
        if (closedFiles.Count > 0)
        {
            closedTransaction = await WorkspaceReplaceFileTransaction.PrepareAsync(
                new ReplacePreview(preview.Replacement, closedFiles),
                cancellationToken);
        }

        RevalidateOpenBuffers(openBuffers, "while the replace operation was being prepared");
        cancellationToken.ThrowIfCancellationRequested();

        if (closedTransaction is not null)
        {
            foreach (var file in closedFiles)
            {
                SuppressWorkspaceChange(file.FilePath);
            }

            await closedTransaction.CommitAsync(cancellationToken);

            try
            {
                // Commit yields to the UI thread, so an editor buffer can change while closed files are
                // being written. If it did, restore every closed file before reporting the conflict.
                RevalidateOpenBuffers(openBuffers, "while closed-file replacements were being committed");
            }
            catch
            {
                foreach (var file in closedFiles)
                {
                    SuppressWorkspaceChange(file.FilePath);
                }

                await closedTransaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        // No awaits occur in this loop, so user edits cannot interleave between validation and applying
        // the coordinated in-memory changes on the WPF dispatcher thread.
        foreach (var item in openBuffers)
        {
            item.Tab.ApplyWorkspaceReplacement(item.ReplacementText);
        }

        return new ReplaceApplyResult(
            (closedTransaction?.FilesChanged ?? 0) + openBuffers.Count,
            (closedTransaction?.MatchesReplaced ?? 0) + openBuffers.Sum(item => item.MatchCount));
    }

    private static void RevalidateOpenBuffers(
        IReadOnlyList<PreparedOpenBuffer> openBuffers,
        string phase)
    {
        foreach (var item in openBuffers)
        {
            if (!item.Tab.AllowLocalEditing)
            {
                throw new InvalidOperationException(
                    $"'{item.Tab.Path}' crossed the local-editing safety limit {phase}. Create a fresh preview before retrying.");
            }

            var currentFingerprint = WorkspaceReplaceText.ComputeFingerprint(item.Tab.Text);
            if (!string.Equals(currentFingerprint, item.ExpectedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The open editor buffer changed {phase}: '{item.Tab.Path}'. Closed-file replacements were not left partially applied; create a fresh preview before retrying.");
            }
        }
    }

    private sealed record PreparedOpenBuffer(
        ViewModels.DocumentTabViewModel Tab,
        string ExpectedFingerprint,
        string ReplacementText,
        int MatchCount);
}
