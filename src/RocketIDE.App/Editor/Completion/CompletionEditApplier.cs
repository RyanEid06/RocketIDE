using ICSharpCode.AvalonEdit.Document;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Editor.Completion;

public static class CompletionEditApplier
{
    public static bool TryApply(
        TextDocument document,
        RocketCompletionItem item,
        int fallbackOffset,
        int fallbackLength,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(item);
        error = string.Empty;

        if (fallbackOffset < 0 || fallbackLength < 0 || fallbackOffset + fallbackLength > document.TextLength)
        {
            error = "Completion fallback range is outside the current document.";
            return false;
        }

        var edits = new List<ResolvedEdit>();
        if (item.TextEdit is { } primary)
        {
            if (!LspTextRangeMapper.TryGetOffsetRange(document, primary.InsertRange, out var offset, out var length))
            {
                error = "Completion text edit insert range is outside the current document or splits a UTF-16 surrogate pair.";
                return false;
            }
            if (!LspTextRangeMapper.TryGetOffsetRange(document, primary.ReplaceRange, out var replaceOffset, out var replaceLength))
            {
                error = "Completion text edit replace range is outside the current document or splits a UTF-16 surrogate pair.";
                return false;
            }
            if (replaceOffset != offset || replaceLength < length)
            {
                error = "Completion text edit insert/replace ranges are inconsistent.";
                return false;
            }
            edits.Add(new ResolvedEdit(offset, length, primary.NewText));
        }
        else
        {
            edits.Add(new ResolvedEdit(fallbackOffset, fallbackLength, item.InsertText));
        }

        foreach (var additional in item.AdditionalTextEdits)
        {
            if (!LspTextRangeMapper.TryGetOffsetRange(document, additional.Range, out var offset, out var length))
            {
                error = "Completion additional text edit is outside the current document or splits a UTF-16 surrogate pair.";
                return false;
            }
            edits.Add(new ResolvedEdit(offset, length, additional.NewText));
        }

        var ascending = edits.OrderBy(edit => edit.Offset).ThenBy(edit => edit.Length).ToArray();
        for (var index = 1; index < ascending.Length; index++)
        {
            if (ascending[index].Offset < ascending[index - 1].EndOffset)
            {
                error = "Completion edits overlap and were rejected.";
                return false;
            }
        }

        using (document.RunUpdate())
        {
            foreach (var edit in ascending.OrderByDescending(edit => edit.Offset))
            {
                document.Replace(edit.Offset, edit.Length, edit.Text);
            }
        }

        return true;
    }

    private sealed record ResolvedEdit(int Offset, int Length, string Text)
    {
        public int EndOffset => Offset + Length;
    }
}
