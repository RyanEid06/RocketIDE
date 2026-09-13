using System.Text;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed class WorkspaceEditValidationException(string message) : Exception(message);

public sealed record WorkspaceEditDocumentSnapshot(
    string Path,
    string Text,
    int? Version,
    bool IsOpen,
    bool IsWritable);

public sealed record WorkspaceEditValidationContext(
    string? WorkspacePath,
    IReadOnlyList<WorkspaceEditDocumentSnapshot> Documents);

public sealed record WorkspaceEditPlannedDocument(
    string Path,
    string OriginalText,
    string ResultText,
    int? OriginalVersion,
    bool IsOpen);

public sealed record WorkspaceEditPlan(IReadOnlyList<WorkspaceEditPlannedDocument> Documents);

public static class WorkspaceEditValidator
{
    public const int MaximumEditCount = 1024;

    public static WorkspaceEditPlan ValidateAndCompute(RocketWorkspaceEdit edit, WorkspaceEditValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Documents);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Documents);

        var totalEdits = edit.Documents.Sum(document => (long)(document.Edits?.Count ?? 0));
        if (totalEdits > MaximumEditCount)
        {
            throw new WorkspaceEditValidationException("WorkspaceEdit exceeds Rocket's 1,024-edit safety limit.");
        }

        var snapshots = new Dictionary<string, WorkspaceEditDocumentSnapshot>(PathComparer);
        foreach (var snapshot in context.Documents)
        {
            var normalized = NormalizePath(snapshot.Path);
            if (!snapshots.TryAdd(normalized, snapshot with { Path = normalized }))
            {
                throw new WorkspaceEditValidationException($"Duplicate document snapshot for '{normalized}'.");
            }
        }

        var grouped = new Dictionary<string, List<RocketWorkspaceDocumentEdit>>(PathComparer);
        foreach (var documentEdit in edit.Documents)
        {
            if (documentEdit.Edits is null)
            {
                throw new WorkspaceEditValidationException("WorkspaceEdit contains a null edit list.");
            }
            var path = NormalizePath(documentEdit.Path);
            if (!grouped.TryGetValue(path, out var items))
            {
                items = [];
                grouped.Add(path, items);
            }
            items.Add(documentEdit with { Path = path });
        }

        var planned = new List<WorkspaceEditPlannedDocument>(grouped.Count);
        foreach (var pair in grouped)
        {
            var path = pair.Key;
            if (!snapshots.TryGetValue(path, out var snapshot))
            {
                throw new WorkspaceEditValidationException($"No validated source snapshot is available for '{path}'.");
            }
            if (!snapshot.IsOpen && !snapshot.IsWritable)
            {
                throw new WorkspaceEditValidationException($"Closed WorkspaceEdit target '{path}' is not writable.");
            }
            if (!snapshot.IsOpen && !IsInsideWorkspace(path, context.WorkspacePath))
            {
                throw new WorkspaceEditValidationException($"Closed WorkspaceEdit target '{path}' is outside the active workspace.");
            }

            var suppliedVersions = pair.Value.Where(item => item.Version.HasValue).Select(item => item.Version!.Value).Distinct().ToArray();
            if (suppliedVersions.Length > 1)
            {
                throw new WorkspaceEditValidationException($"WorkspaceEdit contains conflicting versions for '{path}'.");
            }
            if (suppliedVersions.Length == 1)
            {
                if (!snapshot.IsOpen || !snapshot.Version.HasValue || snapshot.Version.Value != suppliedVersions[0])
                {
                    throw new WorkspaceEditValidationException($"WorkspaceEdit version for '{path}' is stale or cannot be validated.");
                }
            }

            var allEdits = pair.Value.SelectMany(item => item.Edits).ToArray();
            var computed = ComputeDocument(path, snapshot.Text, allEdits);
            ValidateUnicode(path, computed);
            planned.Add(new WorkspaceEditPlannedDocument(path, snapshot.Text, computed, snapshot.Version, snapshot.IsOpen));
        }

        return new WorkspaceEditPlan(planned);
    }

    public static bool IsValidRange(string text, RocketTextEdit edit) =>
        TryGetOffsets(text, edit.Range, out _, out _);

    public static bool IsValidRange(string text, RocketLocation location) =>
        TryGetOffsets(text, location.Range, out _, out _);

    private static string ComputeDocument(string path, string text, IReadOnlyList<RocketTextEdit> edits)
    {
        var resolved = new List<ResolvedEdit>(edits.Count);
        foreach (var edit in edits)
        {
            if (!TryGetOffsets(text, edit.Range, out var start, out var end))
            {
                throw new WorkspaceEditValidationException($"WorkspaceEdit contains an invalid UTF-16 range for '{path}'.");
            }
            resolved.Add(new ResolvedEdit(start, end, edit.NewText ?? string.Empty));
        }

        var ascending = resolved.OrderBy(item => item.Start).ThenBy(item => item.End).ToArray();
        for (var index = 1; index < ascending.Length; index++)
        {
            var previous = ascending[index - 1];
            var current = ascending[index];
            if (current.Start < previous.End || current.Start == previous.Start)
            {
                throw new WorkspaceEditValidationException($"WorkspaceEdit contains overlapping or conflicting edits for '{path}'.");
            }
        }

        var builder = new StringBuilder(text);
        foreach (var item in resolved.OrderByDescending(item => item.Start).ThenByDescending(item => item.End))
        {
            builder.Remove(item.Start, item.End - item.Start);
            builder.Insert(item.Start, item.NewText);
        }
        return builder.ToString();
    }

    private static void ValidateUnicode(string path, string text)
    {
        try
        {
            _ = new UTF8Encoding(false, true).GetByteCount(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new WorkspaceEditValidationException(
                $"WorkspaceEdit result for '{path}' contains invalid Unicode data: {exception.Message}");
        }
    }

    private static bool TryGetOffsets(string text, RocketIDE.Rocket.LanguageServer.LspDtos.LspRange range, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (text is null || range?.Start is null || range.End is null)
        {
            return false;
        }

        var lines = BuildLines(text);
        if (!TryGetOffset(text, lines, range.Start.Line, range.Start.Character, out start) ||
            !TryGetOffset(text, lines, range.End.Line, range.End.Character, out end) || end < start)
        {
            return false;
        }
        return true;
    }

    private static bool TryGetOffset(string text, IReadOnlyList<LineInfo> lines, int line, int character, out int offset)
    {
        offset = 0;
        if (line < 0 || character < 0 || line >= lines.Count)
        {
            return false;
        }
        var info = lines[line];
        if (character > info.Length)
        {
            return false;
        }
        offset = info.Start + character;
        if (character > 0 && character < info.Length &&
            char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]))
        {
            return false;
        }
        return true;
    }

    private static IReadOnlyList<LineInfo> BuildLines(string text)
    {
        var lines = new List<LineInfo>();
        var start = 0;
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] is '\r' or '\n')
            {
                lines.Add(new LineInfo(start, index - start));
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }
                index++;
                start = index;
                continue;
            }
            index++;
        }
        lines.Add(new LineInfo(start, text.Length - start));
        return lines;
    }

    private static bool IsInsideWorkspace(string path, string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return false;
        }
        var root = NormalizePath(workspacePath);
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison) &&
            !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison);
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

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private sealed record ResolvedEdit(int Start, int End, string NewText);
    private sealed record LineInfo(int Start, int Length);
}
