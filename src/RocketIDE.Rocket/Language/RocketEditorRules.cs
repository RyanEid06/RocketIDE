namespace RocketIDE.Rocket.Language;

public static class RocketEditorRules
{
    public const int IndentationSize = 4;

    public static int GetNewLineIndentation(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var current = CountLeadingSpaces(line);
        var code = StripCommentOutsideStrings(line).TrimEnd();
        return code.EndsWith(':') && !EndsWithColonInsideString(line)
            ? current + IndentationSize
            : current;
    }

    public static bool IsDedentKeyword(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var code = StripCommentOutsideStrings(line).Trim();
        return code.StartsWith("else:", StringComparison.Ordinal) ||
            (code.StartsWith("case ", StringComparison.Ordinal) && code.EndsWith(':'));
    }

    public static int GetCurrentLineDedent(string currentLine, string? previousNonBlankLine)
    {
        ArgumentNullException.ThrowIfNull(currentLine);
        if (!IsDedentKeyword(currentLine) || string.IsNullOrWhiteSpace(previousNonBlankLine))
        {
            return 0;
        }

        var currentIndent = CountLeadingSpaces(currentLine);
        if (currentIndent < IndentationSize)
        {
            return 0;
        }

        var previousIndent = CountLeadingSpaces(previousNonBlankLine);
        return currentIndent >= previousIndent ? IndentationSize : 0;
    }

    public static bool IsAutoClosePair(char open, char close) =>
        (open, close) is ('(', ')') or ('[', ']') or ('"', '"') or ('\'', '\'');

    public static char? GetClosingCharacter(char open) => open switch
    {
        '(' => ')',
        '[' => ']',
        '"' => '"',
        '\'' => '\'',
        _ => null,
    };

    public static string WrapSelection(string selection, char open)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var close = GetClosingCharacter(open) ?? throw new ArgumentOutOfRangeException(nameof(open));
        return string.Concat(open, selection, close);
    }

    public static bool IsInStringOrComment(string line, int offset)
    {
        ArgumentNullException.ThrowIfNull(line);
        offset = Math.Clamp(offset, 0, line.Length);
        var quote = '\0';
        var escaped = false;

        for (var index = 0; index < offset; index++)
        {
            var character = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && quote != '\0')
            {
                escaped = true;
                continue;
            }

            if (character is '"' or '\'')
            {
                if (quote == '\0')
                {
                    quote = character;
                }
                else if (quote == character)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character == '#' && quote == '\0')
            {
                return true;
            }
        }

        return quote != '\0';
    }

    private static int CountLeadingSpaces(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static string StripCommentOutsideStrings(string line)
    {
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && quote != '\0')
            {
                escaped = true;
                continue;
            }

            if (character is '"' or '\'')
            {
                if (quote == '\0')
                {
                    quote = character;
                }
                else if (quote == character)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character == '#' && quote == '\0')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static bool EndsWithColonInsideString(string line)
    {
        var trimmed = StripCommentOutsideStrings(line).TrimEnd();
        if (!trimmed.EndsWith(':'))
        {
            return false;
        }

        return IsInStringOrComment(trimmed, trimmed.Length - 1);
    }
}
