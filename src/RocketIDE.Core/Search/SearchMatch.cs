namespace RocketIDE.Core.Search;

public sealed record SearchMatch
{
    public SearchMatch(
        string filePath,
        int line,
        int column,
        int length,
        int startOffset,
        string matchedText,
        string preview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
        ArgumentNullException.ThrowIfNull(matchedText);
        ArgumentNullException.ThrowIfNull(preview);
        FilePath = filePath;
        Line = line;
        Column = column;
        Length = length;
        StartOffset = startOffset;
        MatchedText = matchedText;
        Preview = preview;
    }

    public string FilePath { get; }

    public int Line { get; }

    public int Column { get; }

    public int Length { get; }

    public int StartOffset { get; }

    public string MatchedText { get; }

    public string Preview { get; }
}
