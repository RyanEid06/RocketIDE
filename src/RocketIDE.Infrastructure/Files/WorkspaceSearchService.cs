using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RocketIDE.Core.Search;

namespace RocketIDE.Infrastructure.Files;

public sealed class WorkspaceReplaceConflictException : IOException
{
    public WorkspaceReplaceConflictException(string path)
        : base($"The file changed after the replace preview was created: '{path}'.")
    {
        Path = path;
    }

    public string Path { get; }
}

public sealed class WorkspaceSearchService : IWorkspaceSearchService
{
    public const int DefaultBatchSize = 64;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rocket", ".txt", ".md", ".toml", ".json", ".jsonc", ".cs", ".xaml", ".xml", ".yml", ".yaml",
        ".ps1", ".props", ".targets", ".csproj", ".sln", ".slnx", ".config", ".ini",
    };

    public Task<SearchSummary> SearchAsync(
        SearchQuery query,
        IProgress<SearchResultBatch> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(progress);
        return Task.Run(() => SearchCoreAsync(query, progress, cancellationToken), cancellationToken);
    }

    public Task<ReplacePreview> CreateReplacePreviewAsync(
        SearchQuery query,
        string replacement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        return Task.Run(() => CreateReplacePreviewCoreAsync(query, replacement, cancellationToken), cancellationToken);
    }

    public Task<ReplaceApplyResult> ApplyReplaceAsync(
        ReplacePreview preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return Task.Run(() => ApplyReplaceCoreAsync(preview, cancellationToken), cancellationToken);
    }

    private static async Task<SearchSummary> SearchCoreAsync(
        SearchQuery query,
        IProgress<SearchResultBatch> progress,
        CancellationToken cancellationToken)
    {
        var matches = new List<SearchMatch>(DefaultBatchSize);
        var filesScanned = 0;
        var filesSkipped = 0;
        var totalMatches = 0;
        var reachedResultLimit = false;
        var inMemoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var buffer in query.InMemoryBuffers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bufferPath = Path.GetFullPath(buffer.Key);
            if (!IsUnderRoot(query.RootPath, bufferPath) || !IsSupportedTextFile(bufferPath))
            {
                continue;
            }

            inMemoryPaths.Add(bufferPath);
            filesScanned++;
            foreach (var match in FindMatches(bufferPath, buffer.Value, query))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (totalMatches >= query.MaxResults)
                {
                    reachedResultLimit = true;
                    break;
                }

                matches.Add(match);
                totalMatches++;
                if (matches.Count >= DefaultBatchSize)
                {
                    progress.Report(new SearchResultBatch(matches.ToArray(), filesScanned, filesSkipped, false));
                    matches.Clear();
                }
            }

            if (totalMatches >= query.MaxResults)
            {
                reachedResultLimit = true;
                break;
            }
        }

        foreach (var candidate in EnumerateCandidates(query.RootPath, query.ExcludedDirectoryNames, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.IsSkipped)
            {
                filesSkipped++;
                continue;
            }
            if (inMemoryPaths.Contains(Path.GetFullPath(candidate.Path!)))
            {
                continue;
            }

            var scanned = await ScanFileAsync(candidate.Path!, query, cancellationToken).ConfigureAwait(false);
            if (!scanned.IsReadable)
            {
                filesSkipped++;
                continue;
            }

            filesScanned++;
            foreach (var match in scanned.Matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (totalMatches >= query.MaxResults)
                {
                    reachedResultLimit = true;
                    break;
                }

                matches.Add(match);
                totalMatches++;
                if (matches.Count >= DefaultBatchSize)
                {
                    progress.Report(new SearchResultBatch(matches.ToArray(), filesScanned, filesSkipped, false));
                    matches.Clear();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            if (totalMatches >= query.MaxResults)
            {
                reachedResultLimit = true;
                break;
            }
        }

        progress.Report(new SearchResultBatch(matches.ToArray(), filesScanned, filesSkipped, true, reachedResultLimit));
        cancellationToken.ThrowIfCancellationRequested();
        return new SearchSummary(filesScanned, filesSkipped, totalMatches, reachedResultLimit);
    }

    private static async Task<ReplacePreview> CreateReplacePreviewCoreAsync(
        SearchQuery query,
        string replacement,
        CancellationToken cancellationToken)
    {
        var files = new List<ReplacePreviewFile>();
        var totalMatches = 0;

        foreach (var candidate in EnumerateCandidates(query.RootPath, query.ExcludedDirectoryNames, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.IsSkipped)
            {
                continue;
            }

            var scanned = await ScanFileAsync(candidate.Path!, query, cancellationToken).ConfigureAwait(false);
            if (!scanned.IsReadable || scanned.Matches.Count == 0)
            {
                continue;
            }

            var remaining = query.MaxResults - totalMatches;
            var selected = scanned.Matches.Take(Math.Max(0, remaining)).ToArray();
            if (selected.Length == 0)
            {
                break;
            }

            var fingerprint = await ComputeFingerprintAsync(candidate.Path!, cancellationToken).ConfigureAwait(false);
            files.Add(new ReplacePreviewFile(candidate.Path!, fingerprint, selected));
            totalMatches += selected.Length;
            if (totalMatches >= query.MaxResults)
            {
                break;
            }
        }

        return new ReplacePreview(replacement, files);
    }

    private static async Task<ReplaceApplyResult> ApplyReplaceCoreAsync(
        ReplacePreview preview,
        CancellationToken cancellationToken)
    {
        var prepared = new List<PreparedReplacement>(preview.Files.Count);
        foreach (var file in preview.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(file.FilePath, cancellationToken).ConfigureAwait(false);
            var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(fingerprint, file.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkspaceReplaceConflictException(file.FilePath);
            }

            var (text, hasBom) = Decode(bytes, file.FilePath);
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

            prepared.Add(new PreparedReplacement(file.FilePath, Encode(builder.ToString(), hasBom), file.Matches.Count));
        }

        foreach (var item in prepared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteAtomicallyAsync(item.Path, item.Bytes, cancellationToken).ConfigureAwait(false);
        }

        return new ReplaceApplyResult(prepared.Count, prepared.Sum(item => item.MatchCount));
    }

    private static async Task<ScannedFile> ScanFileAsync(
        string path,
        SearchQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > query.MaxFileBytes || !IsSupportedTextFile(path))
            {
                return ScannedFile.Unreadable;
            }

            var text = await File.ReadAllTextAsync(path, StrictUtf8, cancellationToken).ConfigureAwait(false);
            if (text.IndexOf('\0') >= 0)
            {
                return ScannedFile.Unreadable;
            }

            return new ScannedFile(true, FindMatches(path, text, query));
        }
        catch (DecoderFallbackException)
        {
            return ScannedFile.Unreadable;
        }
        catch (IOException)
        {
            return ScannedFile.Unreadable;
        }
        catch (UnauthorizedAccessException)
        {
            return ScannedFile.Unreadable;
        }
    }

    private static IReadOnlyList<SearchMatch> FindMatches(string path, string text, SearchQuery query)
    {
        var matches = new List<SearchMatch>();
        Regex? regex = null;
        if (query.UseRegex)
        {
            var pattern = query.WholeWord
                ? $"(?<![\\p{{L}}\\p{{N}}_])(?:{query.Pattern})(?![\\p{{L}}\\p{{N}}_])"
                : query.Pattern;
            regex = new Regex(
                pattern,
                (query.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }

        var lineStart = 0;
        var lineNumber = 1;
        while (lineStart <= text.Length && matches.Count < query.MaxResultsPerFile)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var contentEnd = lineEnd > lineStart && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            var line = text[lineStart..contentEnd];
            try
            {
                if (regex is null)
                {
                    AddPlainMatches(matches, path, line, lineStart, lineNumber, query);
                }
                else
                {
                    foreach (Match match in regex.Matches(line))
                    {
                        if (matches.Count >= query.MaxResultsPerFile)
                        {
                            break;
                        }

                        matches.Add(CreateMatch(path, line, lineStart, lineNumber, match.Index, match.Length));
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                break;
            }

            if (lineEnd == text.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
            lineNumber++;
        }

        return matches;
    }

    private static void AddPlainMatches(
        List<SearchMatch> matches,
        string path,
        string line,
        int lineStart,
        int lineNumber,
        SearchQuery query)
    {
        var comparison = query.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var start = 0;
        while (start <= line.Length - query.Pattern.Length && matches.Count < query.MaxResultsPerFile)
        {
            var index = line.IndexOf(query.Pattern, start, comparison);
            if (index < 0)
            {
                break;
            }

            if (!query.WholeWord || IsWholeWord(line, index, query.Pattern.Length))
            {
                matches.Add(CreateMatch(path, line, lineStart, lineNumber, index, query.Pattern.Length));
            }

            start = index + Math.Max(1, query.Pattern.Length);
        }
    }

    private static SearchMatch CreateMatch(
        string path,
        string line,
        int lineStart,
        int lineNumber,
        int index,
        int length)
    {
        var preview = line.Trim();
        return new SearchMatch(path, lineNumber, index + 1, length, lineStart + index, line.Substring(index, length), preview);
    }

    private static bool IsWholeWord(string line, int index, int length) =>
        (index == 0 || !IsWordCharacter(line[index - 1])) &&
        (index + length == line.Length || !IsWordCharacter(line[index + length]));

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static IEnumerable<FileCandidate> EnumerateCandidates(
        string root,
        IReadOnlySet<string> excludedDirectoryNames,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] directories;
            string[] files;
            var inaccessible = false;
            try
            {
                directories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (UnauthorizedAccessException)
            {
                directories = [];
                files = [];
                inaccessible = true;
            }
            catch (IOException)
            {
                directories = [];
                files = [];
                inaccessible = true;
            }

            if (inaccessible)
            {
                yield return FileCandidate.Skipped;
                continue;
            }

            foreach (var child in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (excludedDirectoryNames.Contains(Path.GetFileName(child)))
                {
                    yield return FileCandidate.Skipped;
                }
                else
                {
                    pending.Push(child);
                }
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return IsSupportedTextFile(file) ? new FileCandidate(file, false) : FileCandidate.Skipped;
            }
        }
    }

    private static bool IsSupportedTextFile(string path)
    {
        var name = Path.GetFileName(path);
        return TextExtensions.Contains(Path.GetExtension(name)) ||
            string.Equals(name, ".gitignore", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "LICENSE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return string.Equals(normalizedPath, normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeFingerprintAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
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
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
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

    private sealed record FileCandidate(string? Path, bool IsSkipped)
    {
        public static FileCandidate Skipped { get; } = new(null, true);
    }

    private sealed record ScannedFile(bool IsReadable, IReadOnlyList<SearchMatch> Matches)
    {
        public static ScannedFile Unreadable { get; } = new(false, []);
    }

    private sealed record PreparedReplacement(string Path, byte[] Bytes, int MatchCount);
}
