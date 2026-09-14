using System.Security.Cryptography;
using System.Text;
using RocketIDE.Core.Search;

namespace RocketIDE.Infrastructure.Files;

public sealed class WorkspaceReplaceFileTransaction
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IReadOnlyList<PreparedFile> _files;
    private bool _committed;

    private WorkspaceReplaceFileTransaction(IReadOnlyList<PreparedFile> files)
    {
        _files = files;
    }

    public int FilesChanged => _files.Count;

    public int MatchesReplaced => _files.Sum(file => file.MatchCount);

    public static async Task<WorkspaceReplaceFileTransaction> PrepareAsync(
        ReplacePreview preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.Files.Any(file => file.IsInMemory))
        {
            throw new InvalidOperationException("In-memory replace previews must be coordinated through the editor buffer.");
        }

        var prepared = new List<PreparedFile>(preview.Files.Count);
        foreach (var file in preview.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalBytes = await File.ReadAllBytesAsync(file.FilePath, cancellationToken).ConfigureAwait(false);
            var fingerprint = Convert.ToHexString(SHA256.HashData(originalBytes));
            if (!string.Equals(fingerprint, file.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkspaceReplaceConflictException(file.FilePath);
            }

            var (text, hasBom) = Decode(originalBytes, file.FilePath);
            var builder = new StringBuilder(text);
            foreach (var match in file.Matches.OrderByDescending(match => match.StartOffset))
            {
                if (match.StartOffset < 0 || match.StartOffset + match.Length > builder.Length ||
                    !string.Equals(builder.ToString(match.StartOffset, match.Length), match.MatchedText, StringComparison.Ordinal))
                {
                    throw new WorkspaceReplaceConflictException(file.FilePath);
                }

                builder.Remove(match.StartOffset, match.Length);
                builder.Insert(match.StartOffset, preview.Replacement);
            }

            prepared.Add(new PreparedFile(
                file.FilePath,
                fingerprint,
                originalBytes,
                Encode(builder.ToString(), hasBom),
                file.Matches.Count));
        }

        return new WorkspaceReplaceFileTransaction(prepared);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_committed)
        {
            throw new InvalidOperationException("This replace transaction has already been committed.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Revalidate the complete transaction immediately before the first write. A preview can remain
        // open while files change externally, so preparation alone is not a sufficient conflict check.
        foreach (var file in _files)
        {
            await VerifyUnchangedAsync(file, cancellationToken).ConfigureAwait(false);
        }

        var committed = new List<PreparedFile>(_files.Count);
        try
        {
            foreach (var file in _files)
            {
                // Check again at the last possible point. If a later file changes after the all-file
                // validation, this throws and the already-written files are rolled back below.
                await VerifyUnchangedAsync(file, CancellationToken.None).ConfigureAwait(false);

                // Cancellation stops before commit starts. Once the first write begins, complete or roll back
                // the transaction rather than intentionally leaving a partial replacement behind.
                await WriteAtomicallyAsync(file.Path, file.ReplacementBytes, CancellationToken.None).ConfigureAwait(false);
                committed.Add(file);
            }

            _committed = true;
        }
        catch (Exception commitException)
        {
            var rollbackFailures = await RollBackFilesAsync(committed, CancellationToken.None).ConfigureAwait(false);
            if (rollbackFailures.Count > 0)
            {
                throw new IOException(
                    "Workspace replace failed and one or more files could not be rolled back.",
                    new AggregateException([commitException, .. rollbackFailures]));
            }

            throw;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (!_committed)
        {
            return;
        }

        var failures = await RollBackFilesAsync(_files, cancellationToken).ConfigureAwait(false);
        if (failures.Count > 0)
        {
            throw new IOException(
                "One or more replaced files could not be restored to their pre-replace contents.",
                new AggregateException(failures));
        }

        _committed = false;
    }

    private static async Task<List<Exception>> RollBackFilesAsync(
        IEnumerable<PreparedFile> files,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var file in files.Reverse())
        {
            try
            {
                await WriteAtomicallyAsync(file.Path, file.OriginalBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }

    private static async Task VerifyUnchangedAsync(PreparedFile file, CancellationToken cancellationToken)
    {
        byte[] currentBytes;
        try
        {
            currentBytes = await File.ReadAllBytesAsync(file.Path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            throw new WorkspaceReplaceConflictException(file.Path);
        }
        catch (DirectoryNotFoundException)
        {
            throw new WorkspaceReplaceConflictException(file.Path);
        }

        var currentFingerprint = Convert.ToHexString(SHA256.HashData(currentBytes));
        if (!string.Equals(currentFingerprint, file.ExpectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceReplaceConflictException(file.Path);
        }
    }

    private static (string Text, bool HasBom) Decode(byte[] bytes, string path)
    {
        var preamble = new UTF8Encoding(true).GetPreamble();
        var hasBom = bytes.AsSpan().StartsWith(preamble);
        var offset = hasBom ? preamble.Length : 0;
        try
        {
            return (StrictUtf8.GetString(bytes, offset, bytes.Length - offset), hasBom);
        }
        catch (DecoderFallbackException exception)
        {
            throw new IOException($"Cannot replace '{path}' because it is not valid UTF-8.", exception);
        }
    }

    private static byte[] Encode(string text, bool hasBom)
    {
        var encoding = new UTF8Encoding(hasBom, true);
        var content = encoding.GetBytes(text);
        if (!hasBom)
        {
            return content;
        }

        var preamble = encoding.GetPreamble();
        var bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new IOException($"Cannot determine the parent directory for '{path}'.");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.rocketide-replace-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed record PreparedFile(
        string Path,
        string ExpectedFingerprint,
        byte[] OriginalBytes,
        byte[] ReplacementBytes,
        int MatchCount);
}
