using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Editor.Snippets;

public sealed record RocketSnippetDefinition(string Name, string Trigger, string Description, IReadOnlyList<string> Body);

public static class RocketSnippetCatalog
{
    public static IReadOnlyList<RocketSnippetDefinition> Default { get; } =
    [
        new("Main function", "main", "Create a Rocket entry point",
            ["fn main() -> Int:", "    ${1:print(\"Hello, Rocket!\")}", "    return 0"]),
        new("Function", "fn", "Create a typed Rocket function",
            ["fn ${1:name}(${2:value}: ${3:Int}) -> ${4:Int}:", "    ${5:return value}"]),
        new("Implementation block", "impl", "Create a Rocket method implementation",
            ["impl ${1:Type}:", "    fn ${2:method}(self: ${1:Type}) -> ${3:Int}:", "        ${4:return 0}"]),
        new("Exhaustive match", "match", "Create a match statement",
            ["match ${1:value}:", "    case ${2:Some}(value):", "        ${3:return value}", "    case ${4:None}:", "        ${5:return 0}"]),
        new("Test entry", "testmain", "Create a Rocket test-runner entry point",
            ["fn main() -> Int:", "    if ${1:condition}:", "        return 0", "    return 1"]),
    ];

    public static RocketSnippetDefinition? Find(string trigger) =>
        Default.FirstOrDefault(item => string.Equals(item.Trigger, trigger, StringComparison.OrdinalIgnoreCase));
}

public sealed class RocketSnippetService
{
    private static readonly Regex PlaceholderPattern =
        new(@"\$\{(?<index>\d+)(?::(?<default>[^}]*))?\}|\$(?<final>0)", RegexOptions.Compiled);

    public RocketSnippetSession Insert(EditorViewViewModel view, RocketSnippetDefinition snippet)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(snippet);
        var target = view.CommandTarget ?? throw new InvalidOperationException("The editor view is not currently attached.");
        if (!view.Document.AllowLocalEditing)
            throw new InvalidOperationException("Local editing is disabled for this document.");

        var lineEnding = view.Document.EditorDocument.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var template = string.Join(lineEnding, snippet.Body);
        var expansion = Expand(template);
        var start = Math.Clamp(target.SelectionStart, 0, view.Document.EditorDocument.TextLength);
        var length = Math.Clamp(target.SelectionLength, 0, view.Document.EditorDocument.TextLength - start);

        var undo = view.Document.EditorDocument.UndoStack;
        undo.StartUndoGroup();
        try
        {
            view.Document.EditorDocument.Replace(start, length, expansion.Text);
        }
        finally
        {
            undo.EndUndoGroup();
        }

        view.SnippetSession?.Cancel();
        var session = RocketSnippetSession.Create(view.Document.EditorDocument, target, start, expansion.Placeholders);
        view.SnippetSession = session;
        session.SelectCurrentOrFinal();
        return session;
    }

    public static SnippetExpansion Expand(string template)
    {
        var builder = new StringBuilder();
        var placeholders = new List<SnippetPlaceholder>();
        var position = 0;
        foreach (Match match in PlaceholderPattern.Matches(template))
        {
            builder.Append(template, position, match.Index - position);
            if (match.Groups["final"].Success)
            {
                placeholders.Add(new SnippetPlaceholder(0, builder.Length, 0));
            }
            else
            {
                var index = int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var value = match.Groups["default"].Success ? match.Groups["default"].Value : string.Empty;
                var offset = builder.Length;
                builder.Append(value);
                placeholders.Add(new SnippetPlaceholder(index, offset, value.Length));
            }
            position = match.Index + match.Length;
        }
        builder.Append(template, position, template.Length - position);

        return new SnippetExpansion(
            builder.ToString(),
            placeholders.OrderBy(item => item.Index == 0 ? int.MaxValue : item.Index).ThenBy(item => item.Offset).ToArray());
    }

    public sealed record SnippetExpansion(string Text, IReadOnlyList<SnippetPlaceholder> Placeholders);
    public sealed record SnippetPlaceholder(int Index, int Offset, int Length);
}

public sealed class RocketSnippetSession
{
    private readonly IEditorCommandTarget _target;
    private readonly List<AnchorPair> _stops;
    private int _index;
    private bool _cancelled;

    private RocketSnippetSession(IEditorCommandTarget target, List<AnchorPair> stops)
    {
        _target = target;
        _stops = stops;
    }

    internal static RocketSnippetSession Create(
        TextDocument document,
        IEditorCommandTarget target,
        int insertionOffset,
        IReadOnlyList<RocketSnippetService.SnippetPlaceholder> placeholders)
    {
        var stops = new List<AnchorPair>();
        foreach (var placeholder in placeholders)
        {
            var start = document.CreateAnchor(insertionOffset + placeholder.Offset);
            var end = document.CreateAnchor(insertionOffset + placeholder.Offset + placeholder.Length);
            start.SurviveDeletion = true;
            end.SurviveDeletion = true;
            start.MovementType = AnchorMovementType.BeforeInsertion;
            end.MovementType = AnchorMovementType.AfterInsertion;
            stops.Add(new AnchorPair(placeholder.Index, start, end));
        }

        if (stops.Count == 0)
        {
            var anchor = document.CreateAnchor(insertionOffset);
            anchor.SurviveDeletion = true;
            stops.Add(new AnchorPair(0, anchor, anchor));
        }
        return new RocketSnippetSession(target, stops);
    }

    public bool IsActive => !_cancelled && _index < _stops.Count;

    public bool MoveNext()
    {
        if (!IsActive) return false;
        if (_index + 1 >= _stops.Count)
        {
            Cancel();
            return true;
        }
        _index++;
        SelectCurrentOrFinal();
        return true;
    }

    public bool MovePrevious()
    {
        if (!IsActive || _index == 0) return false;
        _index--;
        SelectCurrentOrFinal();
        return true;
    }

    public void SelectCurrentOrFinal()
    {
        if (!IsActive) return;
        var stop = _stops[_index];
        var start = Math.Min(stop.Start.Offset, stop.End.Offset);
        var length = Math.Abs(stop.End.Offset - stop.Start.Offset);
        _target.SetSelection(start, length);
        _target.FocusEditor();
        if (stop.Index == 0) Cancel();
    }

    public void Cancel() => _cancelled = true;

    private sealed record AnchorPair(int Index, TextAnchor Start, TextAnchor End);
}
