using System.Text;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.Rocket.Language;

namespace RocketIDE.App.Editor;

public static class EditorTextOperations
{
    public static bool ToggleLineComment(TextDocument document, IEditorCommandTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        if (!TryGetSafeSelection(document, target, out var selection))
        {
            return false;
        }

        var (firstLine, lastLine) = GetTargetLines(document, selection.Start, selection.Length);
        var lines = GetLines(document, firstLine.LineNumber, lastLine.LineNumber);
        var lineTexts = lines.Select(line => document.GetText(line.Offset, line.Length)).ToArray();
        var nonBlank = lineTexts
            .Select((text, index) => (text, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.text))
            .ToArray();
        if (nonBlank.Length == 0)
        {
            return false;
        }

        var uncomment = nonBlank.All(item => HasLineComment(item.text));
        var edits = new List<OffsetEdit>();
        var transformed = new string[lineTexts.Length];
        for (var index = 0; index < lineTexts.Length; index++)
        {
            var text = lineTexts[index];
            if (string.IsNullOrWhiteSpace(text))
            {
                transformed[index] = text;
                continue;
            }

            var indentLength = CountLeadingWhitespace(text);
            var absoluteOffset = lines[index].Offset + indentLength;
            if (uncomment)
            {
                var removeLength = RocketEditorConfiguration.LineComment.Length;
                if (text.Length > indentLength + removeLength && text[indentLength + removeLength] == ' ')
                {
                    removeLength++;
                }
                transformed[index] = text.Remove(indentLength, removeLength);
                edits.Add(new OffsetEdit(absoluteOffset, removeLength, 0));
            }
            else
            {
                var insertion = RocketEditorConfiguration.LineComment + " ";
                transformed[index] = text.Insert(indentLength, insertion);
                edits.Add(new OffsetEdit(absoluteOffset, 0, insertion.Length));
            }
        }

        return ReplaceLineBlock(document, target, firstLine, lastLine, transformed, edits, selection);
    }

    public static bool DuplicateLineOrSelection(TextDocument document, IEditorCommandTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        if (!TryGetSafeSelection(document, target, out var selection))
        {
            return false;
        }

        if (selection.Length > 0)
        {
            var selectedText = document.GetText(selection.Start, selection.Length);
            var insertionOffset = selection.Start + selection.Length;
            document.Insert(insertionOffset, selectedText);
            target.SetSelection(insertionOffset, selection.Length);
            target.FocusEditor();
            return true;
        }

        var line = document.GetLineByOffset(selection.Caret);
        var relativeCaret = selection.Caret - line.Offset;
        if (line.DelimiterLength > 0)
        {
            var segmentLength = line.Length + line.DelimiterLength;
            var segment = document.GetText(line.Offset, segmentLength);
            var insertionOffset = line.Offset + segmentLength;
            document.Insert(insertionOffset, segment);
            target.SetSelection(insertionOffset + Math.Min(relativeCaret, line.Length), 0);
            target.FocusEditor();
            return true;
        }

        var newLine = RocketIndentationStrategy.GetPreferredNewLine(document, line.EndOffset);
        var lineText = document.GetText(line.Offset, line.Length);
        var originalLength = document.TextLength;
        document.Insert(originalLength, newLine + lineText);
        target.SetSelection(originalLength + newLine.Length + Math.Min(relativeCaret, line.Length), 0);
        target.FocusEditor();
        return true;
    }

    public static bool MoveLineOrSelectionUp(TextDocument document, IEditorCommandTarget target) =>
        MoveLineOrSelection(document, target, moveDown: false);

    public static bool MoveLineOrSelectionDown(TextDocument document, IEditorCommandTarget target) =>
        MoveLineOrSelection(document, target, moveDown: true);

    public static bool DeleteLineOrSelection(TextDocument document, IEditorCommandTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        if (!TryGetSafeSelection(document, target, out var selection))
        {
            return false;
        }

        if (selection.Length > 0)
        {
            document.Remove(selection.Start, selection.Length);
            target.SetSelection(selection.Start, 0);
            target.FocusEditor();
            return true;
        }

        if (document.TextLength == 0)
        {
            return false;
        }

        var line = document.GetLineByOffset(selection.Caret);
        int removeStart;
        int removeLength;
        if (line.DelimiterLength > 0)
        {
            removeStart = line.Offset;
            removeLength = line.Length + line.DelimiterLength;
        }
        else if (line.LineNumber > 1)
        {
            var previous = document.GetLineByNumber(line.LineNumber - 1);
            removeStart = previous.EndOffset;
            removeLength = document.TextLength - removeStart;
        }
        else
        {
            removeStart = line.Offset;
            removeLength = line.Length;
        }

        if (removeLength == 0)
        {
            return false;
        }

        document.Remove(removeStart, removeLength);
        target.SetSelection(Math.Min(removeStart, document.TextLength), 0);
        target.FocusEditor();
        return true;
    }

    private static bool MoveLineOrSelection(TextDocument document, IEditorCommandTarget target, bool moveDown)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        if (!TryGetSafeSelection(document, target, out var selection))
        {
            return false;
        }

        var (firstLine, lastLine) = GetTargetLines(document, selection.Start, selection.Length);
        if ((!moveDown && firstLine.LineNumber == 1) ||
            (moveDown && lastLine.LineNumber == document.LineCount))
        {
            return false;
        }

        var regionFirstLineNumber = moveDown ? firstLine.LineNumber : firstLine.LineNumber - 1;
        var regionLastLineNumber = moveDown ? lastLine.LineNumber + 1 : lastLine.LineNumber;
        var regionLines = GetLines(document, regionFirstLineNumber, regionLastLineNumber);
        var contents = regionLines.Select(line => document.GetText(line.Offset, line.Length)).ToArray();
        var reordered = new string[contents.Length];
        if (moveDown)
        {
            reordered[0] = contents[^1];
            Array.Copy(contents, 0, reordered, 1, contents.Length - 1);
        }
        else
        {
            Array.Copy(contents, 1, reordered, 0, contents.Length - 1);
            reordered[^1] = contents[0];
        }

        var replacement = BuildRegionReplacement(document, regionLines, reordered);
        var regionStart = regionLines[0].Offset;
        var regionEnd = regionLines[^1].EndOffset;
        var originalBlockStart = firstLine.Offset;
        int newBlockStart;
        if (moveDown)
        {
            var firstDelimiterLength = regionLines[0].DelimiterLength;
            newBlockStart = regionStart + contents[^1].Length + firstDelimiterLength;
        }
        else
        {
            newBlockStart = regionStart;
        }
        var delta = newBlockStart - originalBlockStart;

        document.Replace(regionStart, regionEnd - regionStart, replacement);
        if (selection.Length > 0)
        {
            target.SetSelection(selection.Start + delta, selection.Length);
        }
        else
        {
            target.SetSelection(selection.Caret + delta, 0);
        }
        target.FocusEditor();
        return true;
    }

    private static bool ReplaceLineBlock(
        TextDocument document,
        IEditorCommandTarget target,
        DocumentLine firstLine,
        DocumentLine lastLine,
        IReadOnlyList<string> transformedLineTexts,
        IReadOnlyList<OffsetEdit> edits,
        EditorSelectionSnapshot selection)
    {
        var lines = GetLines(document, firstLine.LineNumber, lastLine.LineNumber);
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
        {
            builder.Append(transformedLineTexts[index]);
            if (index < lines.Count - 1 && lines[index].DelimiterLength > 0)
            {
                builder.Append(document.GetText(lines[index].EndOffset, lines[index].DelimiterLength));
            }
        }

        var replacementStart = firstLine.Offset;
        var replacementLength = lastLine.EndOffset - replacementStart;
        document.Replace(replacementStart, replacementLength, builder.ToString());

        if (selection.Length > 0)
        {
            var mappedStart = MapOffset(selection.Start, edits, moveAtInsertion: true);
            var originalEnd = selection.Start + selection.Length;
            var mappedEnd = MapOffset(originalEnd, edits, moveAtInsertion: false);
            target.SetSelection(mappedStart, Math.Max(0, mappedEnd - mappedStart));
        }
        else
        {
            target.SetSelection(MapOffset(selection.Caret, edits, moveAtInsertion: true), 0);
        }
        target.FocusEditor();
        return true;
    }

    private static string BuildRegionReplacement(
        TextDocument document,
        IReadOnlyList<DocumentLine> regionLines,
        IReadOnlyList<string> reorderedContents)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < reorderedContents.Count; index++)
        {
            builder.Append(reorderedContents[index]);
            if (index < reorderedContents.Count - 1)
            {
                var delimiterSource = regionLines[index];
                if (delimiterSource.DelimiterLength > 0)
                {
                    builder.Append(document.GetText(delimiterSource.EndOffset, delimiterSource.DelimiterLength));
                }
                else
                {
                    builder.Append(RocketIndentationStrategy.GetPreferredNewLine(document, delimiterSource.EndOffset));
                }
            }
        }
        return builder.ToString();
    }

    private static IReadOnlyList<DocumentLine> GetLines(TextDocument document, int firstLineNumber, int lastLineNumber)
    {
        var lines = new List<DocumentLine>(lastLineNumber - firstLineNumber + 1);
        for (var lineNumber = firstLineNumber; lineNumber <= lastLineNumber; lineNumber++)
        {
            lines.Add(document.GetLineByNumber(lineNumber));
        }
        return lines;
    }

    private static (DocumentLine First, DocumentLine Last) GetTargetLines(TextDocument document, int selectionStart, int selectionLength)
    {
        var first = document.GetLineByOffset(Math.Clamp(selectionStart, 0, document.TextLength));
        var endExclusive = Math.Clamp(selectionStart + selectionLength, 0, document.TextLength);
        var lastOffset = selectionLength > 0 ? Math.Max(selectionStart, endExclusive - 1) : selectionStart;
        var last = document.GetLineByOffset(Math.Clamp(lastOffset, 0, document.TextLength));
        return (first, last);
    }

    private static int CountLeadingWhitespace(string text)
    {
        var index = 0;
        while (index < text.Length && text[index] is ' ' or '\t')
        {
            index++;
        }
        return index;
    }

    private static bool HasLineComment(string text)
    {
        var index = CountLeadingWhitespace(text);
        return text.AsSpan(index).StartsWith(RocketEditorConfiguration.LineComment, StringComparison.Ordinal);
    }

    private static int MapOffset(int originalOffset, IReadOnlyList<OffsetEdit> edits, bool moveAtInsertion)
    {
        var delta = 0;
        foreach (var edit in edits.OrderBy(item => item.Offset))
        {
            if (originalOffset < edit.Offset)
            {
                break;
            }

            if (edit.RemovedLength == 0)
            {
                if (originalOffset > edit.Offset || (moveAtInsertion && originalOffset == edit.Offset))
                {
                    delta += edit.InsertedLength;
                }
                continue;
            }

            var removedEnd = edit.Offset + edit.RemovedLength;
            if (originalOffset <= removedEnd)
            {
                return edit.Offset + delta;
            }
            delta += edit.InsertedLength - edit.RemovedLength;
        }
        return originalOffset + delta;
    }

    private static bool TryGetSafeSelection(TextDocument document, IEditorCommandTarget target, out EditorSelectionSnapshot selection)
    {
        var selectionStart = Math.Clamp(target.SelectionStart, 0, document.TextLength);
        var selectionLength = Math.Clamp(target.SelectionLength, 0, document.TextLength - selectionStart);
        var caret = Math.Clamp(target.CaretOffset, 0, document.TextLength);
        selection = new EditorSelectionSnapshot(selectionStart, selectionLength, caret);
        return IsUtf16Boundary(document.Text, selectionStart) &&
               IsUtf16Boundary(document.Text, selectionStart + selectionLength) &&
               IsUtf16Boundary(document.Text, caret);
    }

    private static bool IsUtf16Boundary(string text, int offset) =>
        offset <= 0 || offset >= text.Length || !(char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]));

    private readonly record struct EditorSelectionSnapshot(int Start, int Length, int Caret);
    private readonly record struct OffsetEdit(int Offset, int RemovedLength, int InsertedLength);
}
