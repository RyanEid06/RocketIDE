using System.IO;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Integration;

public sealed class FormatOnSaveService(Action<string> report)
{
    private readonly Action<string> _report = report ?? throw new ArgumentNullException(nameof(report));

    public async Task<RocketSessionDocument> PrepareAsync(
        RocketSessionDocument synchronizedDocument,
        Func<RocketSessionDocument> currentDocumentProvider,
        Func<CancellationToken, Task<IReadOnlyList<RocketTextEdit>?>> requestFormattingAsync,
        Func<IReadOnlyList<RocketTextEdit>, CancellationToken, Task> applyEditsAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synchronizedDocument);
        ArgumentNullException.ThrowIfNull(currentDocumentProvider);
        ArgumentNullException.ThrowIfNull(requestFormattingAsync);
        ArgumentNullException.ThrowIfNull(applyEditsAsync);

        try
        {
            var beforeRequest = currentDocumentProvider();
            if (!Matches(synchronizedDocument, beforeRequest))
            {
                _report($"Format on Save skipped for '{synchronizedDocument.DisplayName}' because the editor changed before formatting started.");
                return beforeRequest;
            }

            var edits = await requestFormattingAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var afterRequest = currentDocumentProvider();
            if (!Matches(synchronizedDocument, afterRequest))
            {
                _report($"Format on Save discarded a stale formatter response for '{synchronizedDocument.DisplayName}'.");
                return afterRequest;
            }

            if (edits is null)
            {
                _report($"Format on Save is unavailable for '{synchronizedDocument.DisplayName}'; saving without formatting.");
                return afterRequest;
            }
            if (edits.Count == 0)
            {
                return afterRequest;
            }

            await applyEditsAsync(edits, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return currentDocumentProvider();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _report($"Format on Save was interrupted for '{synchronizedDocument.DisplayName}': {exception.Message}. Saving without formatting.");
            return currentDocumentProvider();
        }
        catch (Exception exception) when (IsBestEffortFormattingFailure(exception))
        {
            _report($"Format on Save failed for '{synchronizedDocument.DisplayName}': {exception.Message}. Saving without formatting.");
            return currentDocumentProvider();
        }
    }

    private static bool Matches(RocketSessionDocument expected, RocketSessionDocument current) =>
        string.Equals(Path.GetFullPath(expected.Path), Path.GetFullPath(current.Path), PathComparison) &&
        expected.Version == current.Version &&
        string.Equals(expected.Text, current.Text, StringComparison.Ordinal);

    private static bool IsBestEffortFormattingFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or ObjectDisposedException or
            LspProtocolException or JsonRpcResponseException or WorkspaceEditValidationException or WorkspaceEditCommitException;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
