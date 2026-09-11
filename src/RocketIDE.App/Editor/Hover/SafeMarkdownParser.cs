namespace RocketIDE.App.Editor.Hover;

public enum HoverRunKind
{
    Text,
    Code,
    Link,
    LineBreak,
    Html,
}

public sealed record HoverRun(HoverRunKind Kind, string Text, string? Target = null);

public static class SafeMarkdownParser
{
    public static IReadOnlyList<HoverRun> Parse(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return [];
        }

        var runs = new List<HoverRun>();
        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '\n')
            {
                runs.Add(new HoverRun(HoverRunKind.LineBreak, "\n"));
                index++;
                continue;
            }

            if (text[index] == '`')
            {
                var end = text.IndexOf('`', index + 1);
                if (end > index + 1)
                {
                    runs.Add(new HoverRun(HoverRunKind.Code, text[(index + 1)..end]));
                    index = end + 1;
                    continue;
                }
            }

            if (text[index] == '[')
            {
                var closeLabel = text.IndexOf(']', index + 1);
                if (closeLabel > index + 1 && closeLabel + 1 < text.Length && text[closeLabel + 1] == '(')
                {
                    var closeTarget = text.IndexOf(')', closeLabel + 2);
                    if (closeTarget > closeLabel + 2)
                    {
                        runs.Add(new HoverRun(
                            HoverRunKind.Link,
                            text[(index + 1)..closeLabel],
                            text[(closeLabel + 2)..closeTarget]));
                        index = closeTarget + 1;
                        continue;
                    }
                }
            }

            var next = index + 1;
            while (next < text.Length && text[next] is not '\n' and not '`' and not '[')
            {
                next++;
            }
            runs.Add(new HoverRun(HoverRunKind.Text, text[index..next]));
            index = next;
        }

        return runs;
    }
}
