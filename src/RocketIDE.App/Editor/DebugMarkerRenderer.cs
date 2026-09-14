using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace RocketIDE.App.Editor;

/// <summary>
/// Lightweight editor presentation for real debugger state. Breakpoints are shown as an error-accent
/// dot at the left edge of the text viewport; the current stopped line is highlighted with the warning accent.
/// The debugger backend remains authoritative for whether a breakpoint is bound.
/// </summary>
public sealed class DebugMarkerRenderer : IBackgroundRenderer, IDisposable
{
    private readonly TextEditor _editor;
    private IReadOnlySet<int> _breakpointLines = new HashSet<int>();
    private int? _currentLine;
    private bool _disposed;

    public DebugMarkerRenderer(TextEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _editor.TextArea.TextView.BackgroundRenderers.Add(this);
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void UpdateMarkers(IEnumerable<int> breakpointLines, int? currentLine)
    {
        ArgumentNullException.ThrowIfNull(breakpointLines);
        _breakpointLines = breakpointLines.Where(line => line > 0).ToHashSet();
        _currentLine = currentLine is > 0 ? currentLine : null;
        _editor.TextArea.TextView.Redraw();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_disposed) return;
        var breakpointBrush = _editor.TryFindResource("IDE.ErrorBrush") as Brush ?? Brushes.IndianRed;
        var currentBrush = _editor.TryFindResource("IDE.WarningBrush") as Brush ?? Brushes.Goldenrod;
        var currentFill = currentBrush.Clone();
        currentFill.Opacity = 0.12;
        currentFill.Freeze();

        foreach (var lineNumber in _breakpointLines)
        {
            if (!TryGetLineRect(textView, lineNumber, out var rect)) continue;
            drawingContext.DrawEllipse(breakpointBrush, null, new Point(Math.Max(5, rect.Left + 5), rect.Top + Math.Max(5, rect.Height / 2)), 4, 4);
        }

        if (_currentLine is { } current && TryGetLineRect(textView, current, out var currentRect))
        {
            var highlight = new Rect(0, currentRect.Top, Math.Max(textView.ActualWidth, currentRect.Right), Math.Max(1, currentRect.Height));
            drawingContext.DrawRectangle(currentFill, null, highlight);
            var pen = new Pen(currentBrush, 2);
            pen.Freeze();
            drawingContext.DrawLine(pen, new Point(1, currentRect.Top), new Point(1, currentRect.Bottom));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _editor.TextArea.TextView.BackgroundRenderers.Remove(this);
    }

    private bool TryGetLineRect(TextView textView, int oneBasedLine, out Rect rect)
    {
        rect = default;
        if (oneBasedLine < 1 || oneBasedLine > _editor.Document.LineCount) return false;
        var line = _editor.Document.GetLineByNumber(oneBasedLine);
        var length = Math.Max(1, line.Length);
        if (line.Offset + length > _editor.Document.TextLength)
        {
            length = Math.Max(0, _editor.Document.TextLength - line.Offset);
        }
        if (length == 0) return false;
        var segment = new TextSegment { StartOffset = line.Offset, Length = length };
        foreach (var candidate in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            rect = candidate;
            return true;
        }
        return false;
    }
}
