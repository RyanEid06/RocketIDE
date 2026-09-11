using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Editor.Hover;

internal sealed class RocketHoverController : IDisposable
{
    private readonly TextEditor _editor;
    private readonly Func<IRocketEditorFeatureService?> _serviceProvider;
    private readonly ToolTip _toolTip = new();
    private CancellationTokenSource? _requestCancellation;
    private DocumentTabViewModel? _document;
    private bool _disposed;

    public RocketHoverController(TextEditor editor, Func<IRocketEditorFeatureService?> serviceProvider)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _editor.TextArea.TextView.MouseHover += TextView_MouseHover;
        _editor.TextArea.TextView.MouseHoverStopped += TextView_MouseHoverStopped;
        _editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
    }

    public void Attach(DocumentTabViewModel document)
    {
        Detach();
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _editor.Document.Changed += EditorDocument_Changed;
    }

    public void Detach()
    {
        CancelPending();
        _toolTip.IsOpen = false;
        if (_document is not null)
        {
            _editor.Document.Changed -= EditorDocument_Changed;
            _document = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Detach();
        _editor.TextArea.TextView.MouseHover -= TextView_MouseHover;
        _editor.TextArea.TextView.MouseHoverStopped -= TextView_MouseHoverStopped;
        _editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
    }

    private async void TextView_MouseHover(object sender, MouseEventArgs e)
    {
        if (e.Handled || _document is not { } document || _serviceProvider() is not { } service)
        {
            return;
        }

        var textView = _editor.TextArea.TextView;
        var viewPosition = textView.GetPositionFloor(e.GetPosition(textView) + textView.ScrollOffset);
        if (viewPosition is null)
        {
            return;
        }

        CancelPending();
        var cancellation = new CancellationTokenSource();
        _requestCancellation = cancellation;
        var version = document.Version;
        var position = new LspPosition(
            Math.Max(0, viewPosition.Value.Location.Line - 1),
            Math.Max(0, viewPosition.Value.Location.Column - 1));
        try
        {
            var hover = await service.RequestHoverAsync(document.Path, position, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(document, _document) ||
                version != document.Version || hover is null || string.IsNullOrWhiteSpace(hover.Contents.Value))
            {
                return;
            }

            _toolTip.Content = SafeMarkdownPresenter.Create(hover.Contents);
            _toolTip.PlacementTarget = _editor;
            _toolTip.IsOpen = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_requestCancellation, cancellation))
            {
                _requestCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void TextView_MouseHoverStopped(object sender, MouseEventArgs e)
    {
        CancelPending();
        _toolTip.IsOpen = false;
    }

    private void EditorDocument_Changed(object? sender, DocumentChangeEventArgs e)
    {
        CancelPending();
        _toolTip.IsOpen = false;
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        CancelPending();
        _toolTip.IsOpen = false;
    }

    private void CancelPending()
    {
        var cancellation = Interlocked.Exchange(ref _requestCancellation, null);
        cancellation?.Cancel();
    }
}
