using System.IO;
using System.Text;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Integration;

public sealed record WorkspaceEditOpenDocument(string Path, string Text, int Version, Action<string> ApplyText);
public sealed record ClosedWorkspaceFile(string Text, bool HasUtf8Bom);
public sealed record WorkspaceEditApplyResult(bool Succeeded, int ChangedDocumentCount);
public sealed class WorkspaceEditCommitException(string message, Exception innerException) : Exception(message, innerException);

public sealed class WorkspaceEditTransactionService
{
    private readonly Func<IReadOnlyList<WorkspaceEditOpenDocument>> _openDocumentsProvider;
    private readonly Func<string, CancellationToken, Task<ClosedWorkspaceFile>> _readClosedFileAsync;
    private readonly Func<string, bool> _isWritable;
    private readonly Func<string, string, bool, CancellationToken, Task> _writeClosedFileAtomicallyAsync;

    public WorkspaceEditTransactionService(
        Func<IReadOnlyList<WorkspaceEditOpenDocument>> openDocumentsProvider,
        Func<string, CancellationToken, Task<ClosedWorkspaceFile>> readClosedFileAsync,
        Func<string, bool> isWritable,
        Func<string, string, bool, CancellationToken, Task> writeClosedFileAtomicallyAsync)
    {
        _openDocumentsProvider = openDocumentsProvider ?? throw new ArgumentNullException(nameof(openDocumentsProvider));
        _readClosedFileAsync = readClosedFileAsync ?? throw new ArgumentNullException(nameof(readClosedFileAsync));
        _isWritable = isWritable ?? throw new ArgumentNullException(nameof(isWritable));
        _writeClosedFileAtomicallyAsync = writeClosedFileAtomicallyAsync ?? throw new ArgumentNullException(nameof(writeClosedFileAtomicallyAsync));
    }

    public async Task<WorkspaceEditApplyResult> ApplyAsync(
        RocketWorkspaceEdit edit,
        string? workspacePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var openByPath = BuildOpenDocumentMap(_openDocumentsProvider());
        var snapshots = new List<WorkspaceEditDocumentSnapshot>();
        var closedOriginals = new Dictionary<string, ClosedWorkspaceFile>(PathComparer);

        foreach (var target in edit.Documents.Select(item => NormalizePath(item.Path)).Distinct(PathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (openByPath.TryGetValue(target, out var open))
            {
                snapshots.Add(new WorkspaceEditDocumentSnapshot(target, open.Text, open.Version, true, IsWritable: true));
                continue;
            }

            if (!IsInsideWorkspace(target, workspacePath))
            {
                throw new WorkspaceEditValidationException(
                    $"Closed WorkspaceEdit target '{target}' is outside the active workspace.");
            }

            ClosedWorkspaceFile closed;
            try
            {
                closed = await _readClosedFileAsync(target, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new WorkspaceEditValidationException($"WorkspaceEdit target '{target}' could not be read safely: {exception.Message}");
            }
            closedOriginals[target] = closed;
            snapshots.Add(new WorkspaceEditDocumentSnapshot(target, closed.Text, null, false, _isWritable(target)));
        }

        var plan = WorkspaceEditValidator.ValidateAndCompute(edit, new WorkspaceEditValidationContext(workspacePath, snapshots));
        var changed = plan.Documents.Where(item => !string.Equals(item.OriginalText, item.ResultText, StringComparison.Ordinal)).ToArray();
        if (changed.Length == 0)
        {
            return new WorkspaceEditApplyResult(true, 0);
        }

        await ValidateSourcesUnchangedAsync(changed, openByPath, closedOriginals, cancellationToken);

        var committedClosed = new List<WorkspaceEditPlannedDocument>();
        var committedOpen = new List<WorkspaceEditPlannedDocument>();
        try
        {
            foreach (var document in changed.Where(item => !item.IsOpen))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var original = closedOriginals[document.Path];
                await _writeClosedFileAtomicallyAsync(
                    document.Path,
                    document.ResultText,
                    original.HasUtf8Bom,
                    cancellationToken);
                committedClosed.Add(document);
            }

            ValidateOpenSourcesUnchanged(changed, BuildOpenDocumentMap(_openDocumentsProvider()));

            foreach (var document in changed.Where(item => item.IsOpen))
            {
                cancellationToken.ThrowIfCancellationRequested();
                openByPath[document.Path].ApplyText(document.ResultText);
                committedOpen.Add(document);
            }

            return new WorkspaceEditApplyResult(true, changed.Length);
        }
        catch (Exception exception)
        {
            var rollbackProblems = await RollbackAsync(committedOpen, committedClosed, openByPath, closedOriginals);
            if (exception is OperationCanceledException && rollbackProblems.Count == 0)
            {
                throw;
            }

            var suffix = rollbackProblems.Count == 0
                ? "Original snapshots were restored."
                : $"Rollback also reported: {string.Join(" | ", rollbackProblems)}";
            throw new WorkspaceEditCommitException($"WorkspaceEdit commit failed. {suffix}", exception);
        }
    }

    public static WorkspaceEditTransactionService CreateFileSystemService(
        Func<IReadOnlyList<WorkspaceEditOpenDocument>> openDocumentsProvider) =>
        new(openDocumentsProvider, ReadClosedFileAsync, IsWritableFile, WriteClosedFileAtomicallyAsync);

    private async Task ValidateSourcesUnchangedAsync(
        IReadOnlyList<WorkspaceEditPlannedDocument> changed,
        IReadOnlyDictionary<string, WorkspaceEditOpenDocument> originalOpen,
        IReadOnlyDictionary<string, ClosedWorkspaceFile> closedOriginals,
        CancellationToken cancellationToken)
    {
        foreach (var document in changed.Where(item => !item.IsOpen))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClosedWorkspaceFile current;
            try
            {
                current = await _readClosedFileAsync(document.Path, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new WorkspaceEditValidationException(
                    $"WorkspaceEdit target '{document.Path}' changed or became unreadable before commit: {exception.Message}");
            }

            var original = closedOriginals[document.Path];
            if (!string.Equals(current.Text, original.Text, StringComparison.Ordinal) || current.HasUtf8Bom != original.HasUtf8Bom)
            {
                throw new WorkspaceEditValidationException(
                    $"WorkspaceEdit target '{document.Path}' changed after validation and before commit.");
            }
            if (!_isWritable(document.Path))
            {
                throw new WorkspaceEditValidationException(
                    $"Closed WorkspaceEdit target '{document.Path}' became unwritable before commit.");
            }
        }

        var currentOpen = BuildOpenDocumentMap(_openDocumentsProvider());
        ValidateOpenSourcesUnchanged(changed, currentOpen);
        foreach (var original in originalOpen)
        {
            if (changed.Any(document => document.IsOpen && PathComparer.Equals(document.Path, original.Key)) &&
                (!currentOpen.TryGetValue(original.Key, out var current) ||
                 current.Version != original.Value.Version ||
                 !string.Equals(current.Text, original.Value.Text, StringComparison.Ordinal)))
            {
                throw new WorkspaceEditValidationException(
                    $"Open WorkspaceEdit target '{original.Key}' changed after its transaction snapshot was captured.");
            }
        }
    }

    private static void ValidateOpenSourcesUnchanged(
        IReadOnlyList<WorkspaceEditPlannedDocument> changed,
        IReadOnlyDictionary<string, WorkspaceEditOpenDocument> currentOpen)
    {
        foreach (var document in changed)
        {
            if (document.IsOpen)
            {
                if (!currentOpen.TryGetValue(document.Path, out var current) ||
                    current.Version != document.OriginalVersion ||
                    !string.Equals(current.Text, document.OriginalText, StringComparison.Ordinal))
                {
                    throw new WorkspaceEditValidationException(
                        $"Open WorkspaceEdit target '{document.Path}' changed after validation and before commit.");
                }
            }
            else if (currentOpen.ContainsKey(document.Path))
            {
                throw new WorkspaceEditValidationException(
                    $"WorkspaceEdit target '{document.Path}' became open after validation and before commit.");
            }
        }
    }

    private static IReadOnlyDictionary<string, WorkspaceEditOpenDocument> BuildOpenDocumentMap(
        IReadOnlyList<WorkspaceEditOpenDocument> documents)
    {
        var result = new Dictionary<string, WorkspaceEditOpenDocument>(PathComparer);
        foreach (var document in documents)
        {
            var path = NormalizePath(document.Path);
            if (!result.TryAdd(path, document with { Path = path }))
            {
                throw new WorkspaceEditValidationException($"Duplicate open document snapshot for '{path}'.");
            }
        }
        return result;
    }

    private async Task<IReadOnlyList<string>> RollbackAsync(
        IEnumerable<WorkspaceEditPlannedDocument> committedOpen,
        IEnumerable<WorkspaceEditPlannedDocument> committedClosed,
        IReadOnlyDictionary<string, WorkspaceEditOpenDocument> openByPath,
        IReadOnlyDictionary<string, ClosedWorkspaceFile> closedOriginals)
    {
        var failures = new List<string>();
        foreach (var document in committedOpen.Reverse())
        {
            try
            {
                openByPath[document.Path].ApplyText(document.OriginalText);
            }
            catch (Exception exception)
            {
                failures.Add($"open '{document.Path}': {exception.Message}");
            }
        }

        foreach (var document in committedClosed.Reverse())
        {
            try
            {
                var original = closedOriginals[document.Path];
                await _writeClosedFileAtomicallyAsync(document.Path, original.Text, original.HasUtf8Bom, CancellationToken.None);
            }
            catch (Exception exception)
            {
                failures.Add($"closed '{document.Path}': {exception.Message}");
            }
        }
        return failures;
    }

    private static async Task<ClosedWorkspaceFile> ReadClosedFileAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            throw new IOException("File contains NUL bytes and is not safe text.");
        }
        var bom = Encoding.UTF8.GetPreamble();
        var hasBom = bytes.AsSpan().StartsWith(bom);
        var offset = hasBom ? bom.Length : 0;
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            return new ClosedWorkspaceFile(text, hasBom);
        }
        catch (DecoderFallbackException exception)
        {
            throw new IOException("File is not valid UTF-8.", exception);
        }
    }

    private static bool IsWritableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                return false;
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return stream.CanWrite;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static async Task WriteClosedFileAtomicallyAsync(
        string path,
        string text,
        bool includeBom,
        CancellationToken cancellationToken)
    {
        var encoding = new UTF8Encoding(includeBom, true);
        byte[] content;
        try
        {
            content = encoding.GetBytes(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new IOException("WorkspaceEdit result contains invalid Unicode data.", exception);
        }
        var bytes = includeBom ? encoding.GetPreamble().Concat(content).ToArray() : content;
        var directory = Path.GetDirectoryName(path) ?? throw new IOException($"Cannot determine parent directory for '{path}'.");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.rocketide-edit-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new WorkspaceEditValidationException($"WorkspaceEdit path '{path}' is invalid: {exception.Message}");
        }
    }

    private static bool IsInsideWorkspace(string path, string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return false;
        }
        var root = Path.GetFullPath(workspacePath);
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
