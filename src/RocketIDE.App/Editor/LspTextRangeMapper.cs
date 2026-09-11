using ICSharpCode.AvalonEdit.Document;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Editor;

internal static class LspTextRangeMapper
{
    public static bool TryGetOffset(TextDocument document, LspPosition position, out int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(position);
        return TryGetOffset(document, position.Line, position.Character, out offset);
    }

    public static bool TryGetOffsetRange(TextDocument document, LspRange range, out int startOffset, out int length)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(range);
        startOffset = 0;
        length = 0;
        if (!TryGetOffset(document, range.Start.Line, range.Start.Character, out var start) ||
            !TryGetOffset(document, range.End.Line, range.End.Character, out var end) ||
            end < start)
        {
            return false;
        }

        startOffset = start;
        length = end - start;
        return true;
    }

    public static bool TryGetOffset(TextDocument document, int zeroBasedLine, int utf16Character, out int offset)
    {
        offset = 0;
        if (zeroBasedLine < 0 || zeroBasedLine >= document.LineCount || utf16Character < 0)
        {
            return false;
        }

        var line = document.GetLineByNumber(zeroBasedLine + 1);
        if (utf16Character > line.Length)
        {
            return false;
        }

        offset = line.Offset + utf16Character;
        if (offset > line.Offset && offset < line.EndOffset &&
            char.IsHighSurrogate(document.GetCharAt(offset - 1)) &&
            char.IsLowSurrogate(document.GetCharAt(offset)))
        {
            offset = 0;
            return false;
        }

        return true;
    }
}
