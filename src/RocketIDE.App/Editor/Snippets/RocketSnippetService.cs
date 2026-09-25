using System.IO;
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
            ["fn main() -> Int:", "    ${1:print(\"Hello, Rocket!\")}", "    return 0$0"]),
        new("Function", "fn", "Create a typed Rocket function",
            ["fn ${1:name}(${2:value}: ${3:Int}) -> ${4:Int}:", "    ${5:return value}$0"]),
        new("Implementation block", "impl", "Create a Rocket method implementation",
            ["impl ${1:Type}:", "    fn ${2:method}(self: ${1:Type}) -> ${3:Int}:", "        ${4:return 0}$0"]),
        new("Exhaustive match", "match", "Create a match statement",
            ["match ${1:value}:", "    case ${2:Some}(value):", "        ${3:return value}", "    case ${4:None}:", "        ${5:return 0}$0"]),
        new("Test entry", "testmain", "Create a Rocket test-runner entry point",
            ["fn main() -> Int:", "    if ${1:condition}:", "        return 0", "    return 1$0"]),
    ];

    public static RocketSnippetDefinition? Find(string trigger) =>
        Default.FirstOrDefault(item => string.Equals(item.Trigger, trigger, StringComparison.OrdinalIgnoreCase));
}

public sealed class RocketSnippetService
{
    private static readonly Regex PlaceholderPattern =
        new(@"\$\{(?<index>\d+)(?::(?<default>[^}]*))?\}|\$(?<final>0)", RegexOptions.Compiled);

    public static bool CanInsert(EditorViewViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.CommandTarget is not null &&
               view.Document.AllowLocalEditing &&
               string.Equals(Path.GetExtension(view.Document.Path), ".rocket", StringComparison.OrdinalIgnoreCase);
    }

    public RocketSnippetSession Insert(EditorViewViewModel view, RocketSnippetDefinition snippet)
    {
        ArgumentNullException.ThrowIfNull(view);
        var target = view.CommandTarget ?? throw new InvalidOperationException("The editor view is not currently attached.");
        return Insert(view, snippet, target.SelectionStart, target.SelectionLength);
    }

    public RocketSnippetSession Insert(EditorViewViewModel view, RocketSnippetDefinition snippet, int startOffset, int length)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(snippet);
        if (!CanInsert(view))
            throw new InvalidOperationException("Rocket snippets require an attached, locally editable .rocket editor.");

        var target = view.CommandTarget!;
        var lineEnding = view.Document.EditorDocument.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var template = string.Join(lineEnding, snippet.Body);
        var expansion = Expand(template);
        var start = Math.Clamp(startOffset, 0, view.Document.EditorDocument.TextLength);
        var clampedLength = Math.Clamp(length, 0, view.Document.EditorDocument.TextLength - start);

        var undo = view.Document.EditorDocument.UndoStack;
        undo.StartUndoGroup();
        try
        {
            view.Document.EditorDocument.Replace(start, clampedLength, expansion.Text);
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
    private readonly TextDocument _document;
    private readonly IEditorCommandTarget _target;
    private readonly List<AnchorGroup> _stops;
    private int _index;
    private bool _cancelled;

    private RocketSnippetSession(TextDocument document, IEditorCommandTarget target, List<AnchorGroup> stops)
    {
        _document = document;
        _target = target;
        _stops = stops;
    }

    internal static RocketSnippetSession Create(
        TextDocument document,
        IEditorCommandTarget target,
        int insertionOffset,
        IReadOnlyList<RocketSnippetService.SnippetPlaceholder> placeholders)
    {
        var stops = placeholders
            .GroupBy(placeholder => placeholder.Index)
            .OrderBy(group => group.Key == 0 ? int.MaxValue : group.Key)
            .Select(group => new AnchorGroup(
                group.Key,
                group.Select(placeholder => CreateAnchorPair(document, insertionOffset, placeholder)).ToList()))
            .ToList();

        if (stops.Count == 0)
        {
            var anchor = document.CreateAnchor(insertionOffset);
            anchor.SurviveDeletion = true;
            stops.Add(new AnchorGroup(0, [new AnchorPair(anchor, anchor)]));
        }

        return new RocketSnippetSession(document, target, stops);
    }

    public bool IsActive => !_cancelled && _index < _stops.Count;

    public bool MoveNext()
    {
        if (!IsActive) return false;
        SynchronizeCurrentLinkedStop();
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
        SynchronizeCurrentLinkedStop();
        _index--;
        SelectCurrentOrFinal();
        return true;
    }

    public void SelectCurrentOrFinal()
    {
        if (!IsActive) return;
        var stop = _stops[_index];
        var primary = stop.Occurrences[0];
        var start = Math.Min(primary.Start.Offset, primary.End.Offset);
        var length = Math.Abs(primary.End.Offset - primary.Start.Offset);
        _target.SetSelection(start, length);
        _target.FocusEditor();
        if (stop.Index == 0) Cancel();
    }

    public void Cancel()
    {
        if (_cancelled) return;
        if (_index < _stops.Count && _stops[_index].Index != 0)
            SynchronizeCurrentLinkedStop();
        _cancelled = true;
    }

    private void SynchronizeCurrentLinkedStop()
    {
        if (!IsActive) return;
        var stop = _stops[_index];
        if (stop.Index == 0 || stop.Occurrences.Count < 2) return;

        var primary = stop.Occurrences[0];
        var sourceStart = Math.Min(primary.Start.Offset, primary.End.Offset);
        var sourceLength = Math.Abs(primary.End.Offset - primary.Start.Offset);
        var text = _document.GetText(sourceStart, sourceLength);

        using (_document.RunUpdate())
        {
            foreach (var occurrence in stop.Occurrences.Skip(1).OrderByDescending(pair => Math.Min(pair.Start.Offset, pair.End.Offset)))
            {
                var start = Math.Min(occurrence.Start.Offset, occurrence.End.Offset);
                var length = Math.Abs(occurrence.End.Offset - occurrence.Start.Offset);
                if (length == text.Length && string.Equals(_document.GetText(start, length), text, StringComparison.Ordinal))
                    continue;
                _document.Replace(start, length, text);
            }
        }
    }

    private static AnchorPair CreateAnchorPair(
        TextDocument document,
        int insertionOffset,
        RocketSnippetService.SnippetPlaceholder placeholder)
    {
        var start = document.CreateAnchor(insertionOffset + placeholder.Offset);
        var end = document.CreateAnchor(insertionOffset + placeholder.Offset + placeholder.Length);
        start.SurviveDeletion = true;
        end.SurviveDeletion = true;
        start.MovementType = AnchorMovementType.BeforeInsertion;
        end.MovementType = AnchorMovementType.AfterInsertion;
        return new AnchorPair(start, end);
    }

    private sealed record AnchorGroup(int Index, List<AnchorPair> Occurrences);
    private sealed record AnchorPair(TextAnchor Start, TextAnchor End);
}
