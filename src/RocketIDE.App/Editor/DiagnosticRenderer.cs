using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using RocketIDE.Core.Diagnostics;

namespace RocketIDE.App.Editor;

public sealed class DiagnosticMarkerCollection
{
    private IReadOnlyList<DiagnosticMarker> _markers = [];

    public IReadOnlyList<DiagnosticMarker> Markers => _markers;

    public void Update(TextDocument document, IReadOnlyList<RocketDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var markers = new List<DiagnosticMarker>(diagnostics.Count);
        foreach (var diagnostic in diagnostics)
        {
            if (!DiagnosticRenderer.TryGetOffsetRange(document, diagnostic.Range, out var offset, out var length))
            {
                continue;
            }

            if (length == 0 && offset < document.TextLength)
            {
                length = 1;
            }

            if (length > 0)
            {
                markers.Add(new DiagnosticMarker(offset, length, diagnostic));
            }
        }

        _markers = markers;
    }

    public void Clear() => _markers = [];
}

public sealed class DiagnosticMarker(int offset, int length, RocketDiagnostic diagnostic) : ISegment
{
    public int Offset { get; } = offset;
    public int Length { get; } = length;
    public int EndOffset => Offset + Length;
    public RocketDiagnostic Diagnostic { get; } = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
}

public sealed class DiagnosticRenderer : IBackgroundRenderer, IDisposable
{
    private readonly TextEditor _editor;
    private readonly ToolTip _toolTip = new();
    private readonly DiagnosticMarkerCollection _markers = new();
    private bool _disposed;

    public DiagnosticRenderer(TextEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _editor.TextArea.TextView.BackgroundRenderers.Add(this);
        _editor.TextArea.TextView.MouseHover += TextView_MouseHover;
        _editor.TextArea.TextView.MouseHoverStopped += TextView_MouseHoverStopped;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void UpdateDiagnostics(IReadOnlyList<RocketDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _markers.Update(_editor.Document, diagnostics);
        _toolTip.IsOpen = false;
        _editor.TextArea.TextView.Redraw();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);
        foreach (var marker in _markers.Markers)
        {
            var brush = SeverityBrush(marker.Diagnostic.Severity);
            var pen = new Pen(brush, 1);
            pen.Freeze();
            foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, marker))
            {
                DrawSquiggle(drawingContext, rectangle.BottomLeft, rectangle.BottomRight, pen);
            }
        }
    }

    public static bool TryGetOffsetRange(TextDocument document, SourceRange range, out int startOffset, out int length)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(range);
        startOffset = 0;
        length = 0;
        if (!TryGetOffset(document, range.StartLine, range.StartCharacter, out var start) ||
            !TryGetOffset(document, range.EndLine, range.EndCharacter, out var end) ||
            end < start)
        {
            return false;
        }

        startOffset = start;
        length = end - start;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _markers.Clear();
        _toolTip.IsOpen = false;
        _editor.TextArea.TextView.MouseHover -= TextView_MouseHover;
        _editor.TextArea.TextView.MouseHoverStopped -= TextView_MouseHoverStopped;
        _editor.TextArea.TextView.BackgroundRenderers.Remove(this);
    }

    private static bool TryGetOffset(TextDocument document, int zeroBasedLine, int utf16Character, out int offset) =>
        LspTextRangeMapper.TryGetOffset(document, zeroBasedLine, utf16Character, out offset);

    private void TextView_MouseHover(object sender, MouseEventArgs e)
    {
        var textView = _editor.TextArea.TextView;
        var position = textView.GetPositionFloor(e.GetPosition(textView) + textView.ScrollOffset);
        if (position is null)
        {
            return;
        }

        var offset = _editor.Document.GetOffset(position.Value.Location);
        var marker = _markers.Markers.FirstOrDefault(item => offset >= item.Offset && offset < item.EndOffset);
        if (marker is null)
        {
            return;
        }

        var diagnostic = marker.Diagnostic;
        var title = string.IsNullOrWhiteSpace(diagnostic.Code) ? diagnostic.Source : $"{diagnostic.Code} · {diagnostic.Source}";
        _toolTip.Content = $"{title}\n{diagnostic.Message}";
        _toolTip.PlacementTarget = _editor;
        _toolTip.IsOpen = true;
        e.Handled = true;
    }

    private void TextView_MouseHoverStopped(object sender, MouseEventArgs e) => _toolTip.IsOpen = false;

    private Brush SeverityBrush(DiagnosticSeverity severity)
    {
        var resourceKey = severity switch
        {
            DiagnosticSeverity.Error => "IDE.ErrorBrush",
            DiagnosticSeverity.Warning => "IDE.WarningBrush",
            DiagnosticSeverity.Information => "IDE.AccentBrush",
            DiagnosticSeverity.Hint => "IDE.TextMutedBrush",
            _ => "IDE.ErrorBrush",
        };
        return _editor.TryFindResource(resourceKey) as Brush ?? Brushes.IndianRed;
    }

    private static void DrawSquiggle(DrawingContext context, Point start, Point end, Pen pen)
    {
        const double step = 2.5;
        if (end.X <= start.X)
        {
            return;
        }

        var points = new List<Point>();
        var x = start.X;
        var raised = true;
        while (x < end.X)
        {
            x = Math.Min(x + step, end.X);
            points.Add(new Point(x, start.Y - (raised ? step : 0)));
            raised = !raised;
        }

        var geometry = new StreamGeometry();
        using (var contextWriter = geometry.Open())
        {
            contextWriter.BeginFigure(start, isFilled: false, isClosed: false);
            contextWriter.PolyLineTo(points, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        context.DrawGeometry(Brushes.Transparent, pen, geometry);
    }
}
