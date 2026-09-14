using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace RocketIDE.App.Editor;

public readonly record struct BracketPair(int OpenOffset, int CloseOffset);

public static class BracketMatcher
{
    public static bool TryFindAtCaret(string text, int caretOffset, out BracketPair pair)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (TryFind(text, caretOffset, out pair))
        {
            return true;
        }

        return caretOffset > 0 && TryFind(text, caretOffset - 1, out pair);
    }

    public static bool TryFind(string text, int offset, out BracketPair pair)
    {
        ArgumentNullException.ThrowIfNull(text);
        pair = default;
        if (offset < 0 || offset >= text.Length || text.Length > 100_000)
        {
            return false;
        }

        var stack = new Stack<(int Offset, char Character)>();
        var matches = new Dictionary<int, int>();
        var lexical = LexicalState.Normal;
        for (var index = 0; index < text.Length; index++)
        {
            if (!AdvanceLexicalState(text, ref index, ref lexical)) continue;
            var character = text[index];
            if (IsOpening(character))
            {
                stack.Push((index, character));
                continue;
            }

            if (IsClosing(character) && stack.Count > 0 && ClosingFor(stack.Peek().Character) == character)
            {
                var opening = stack.Pop();
                matches[opening.Offset] = index;
                matches[index] = opening.Offset;
            }
        }

        if (!matches.TryGetValue(offset, out var other))
        {
            return false;
        }

        pair = offset < other ? new BracketPair(offset, other) : new BracketPair(other, offset);
        return true;
    }

    private static bool AdvanceLexicalState(string text, ref int index, ref LexicalState state)
    {
        var character = text[index];
        if (state == LexicalState.LineComment)
        {
            if (character == '\n') state = LexicalState.Normal;
            return false;
        }

        if (state is LexicalState.SingleQuotedString or LexicalState.DoubleQuotedString)
        {
            if (character == '\\')
            {
                if (index + 1 < text.Length) index++;
                return false;
            }

            var quote = state == LexicalState.SingleQuotedString ? '\'' : '"';
            if (character == quote) state = LexicalState.Normal;
            return false;
        }

        if (character == '#')
        {
            state = LexicalState.LineComment;
            return false;
        }

        if (character == '\'')
        {
            state = LexicalState.SingleQuotedString;
            return false;
        }

        if (character == '"')
        {
            state = LexicalState.DoubleQuotedString;
            return false;
        }

        return true;
    }

    private static bool IsOpening(char character) => character is '(' or '[' or '{';
    private static bool IsClosing(char character) => character is ')' or ']' or '}';
    private static char ClosingFor(char character) => character switch { '(' => ')', '[' => ']', '{' => '}', _ => '\0' };
    private enum LexicalState { Normal, SingleQuotedString, DoubleQuotedString, LineComment }
}

public sealed class BracketMatchRenderer : IBackgroundRenderer, IDisposable
{
    private readonly TextEditor _editor;
    private BracketPair? _pair;
    private bool _disposed;

    public BracketMatchRenderer(TextEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _editor.TextArea.TextView.BackgroundRenderers.Add(this);
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Update()
    {
        if (_disposed || _editor.Document.TextLength > 100_000)
        {
            _pair = null;
        }
        else if (BracketMatcher.TryFindAtCaret(_editor.Document.Text, _editor.CaretOffset, out var pair))
        {
            _pair = pair;
        }
        else
        {
            _pair = null;
        }

        _editor.TextArea.TextView.Redraw();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_pair is not { } pair) return;
        var brush = _editor.TryFindResource("IDE.AccentBrush") as Brush ?? Brushes.DeepSkyBlue;
        var pen = new Pen(brush, 1);
        pen.Freeze();
        DrawOffset(textView, drawingContext, pair.OpenOffset, pen);
        DrawOffset(textView, drawingContext, pair.CloseOffset, pen);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _editor.TextArea.TextView.BackgroundRenderers.Remove(this);
    }

    private static void DrawOffset(TextView textView, DrawingContext context, int offset, Pen pen)
    {
        var segment = new TextSegment { StartOffset = offset, Length = 1 };
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            context.DrawRectangle(Brushes.Transparent, pen, rect);
        }
    }
}
