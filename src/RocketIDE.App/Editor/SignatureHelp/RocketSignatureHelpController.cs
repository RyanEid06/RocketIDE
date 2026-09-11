using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Editor.SignatureHelp;

internal sealed class RocketSignatureHelpController : IDisposable
{
    private static readonly TimeSpan RetriggerDebounce = TimeSpan.FromMilliseconds(90);
    private readonly TextEditor _editor;
    private readonly Func<IRocketEditorFeatureService?> _serviceProvider;
    private readonly ToolTip _toolTip = new() { StaysOpen = true };
    private CancellationTokenSource? _requestCancellation;
    private DocumentTabViewModel? _document;
    private bool _sessionActive;
    private bool _disposed;

    public RocketSignatureHelpController(TextEditor editor, Func<IRocketEditorFeatureService?> serviceProvider)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _editor.TextArea.TextEntered += TextArea_TextEntered;
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
        _sessionActive = false;
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
        _editor.TextArea.TextEntered -= TextArea_TextEntered;
        _editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
    }

    private void TextArea_TextEntered(object sender, TextCompositionEventArgs e) =>
        HandleTextInput(e.Text);

    internal void NotifyHandledTextInput(string text) =>
        HandleTextInput(text);

    private void HandleTextInput(string text)
    {
        if (_document is null || string.IsNullOrEmpty(text) || _serviceProvider() is not { } service)
        {
            return;
        }

        var isTrigger = service.SignatureTriggerCharacters.Contains(text, StringComparer.Ordinal);
        var isRetriggerCharacter = service.SignatureRetriggerCharacters.Contains(text, StringComparer.Ordinal);
        if (!isTrigger && !isRetriggerCharacter)
        {
            return;
        }

        var isRetrigger = _sessionActive || isRetriggerCharacter;
        _sessionActive = true;
        _ = ScheduleRequestAsync(text, isRetrigger, TimeSpan.Zero);
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        CancelPending();
        _toolTip.IsOpen = false;
        if (_sessionActive && _document is not null)
        {
            _ = ScheduleRequestAsync(null, isRetrigger: true, RetriggerDebounce);
        }
    }

    private void EditorDocument_Changed(object? sender, DocumentChangeEventArgs e)
    {
        CancelPending();
        _toolTip.IsOpen = false;
    }

    private async Task ScheduleRequestAsync(string? triggerCharacter, bool isRetrigger, TimeSpan delay)
    {
        var document = _document;
        var service = _serviceProvider();
        if (document is null || service is null)
        {
            return;
        }

        CancelPending();
        var cancellation = new CancellationTokenSource();
        _requestCancellation = cancellation;
        var requestVersion = document.Version;
        var caretOffset = _editor.TextArea.Caret.Offset;
        var position = new LspPosition(
            Math.Max(0, _editor.TextArea.Caret.Line - 1),
            Math.Max(0, _editor.TextArea.Caret.Column - 1));
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellation.Token);
            }
            var help = await service.RequestSignatureHelpAsync(document.Path, position, triggerCharacter, isRetrigger, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(document, _document) ||
                requestVersion != document.Version || caretOffset != _editor.TextArea.Caret.Offset)
            {
                return;
            }

            if (help is null || help.Signatures.Count == 0)
            {
                _sessionActive = false;
                _toolTip.IsOpen = false;
                return;
            }

            _sessionActive = true;
            _toolTip.Content = SignatureHelpPresenter.Create(help);
            PositionToolTipAtCaret();
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

    private void PositionToolTipAtCaret()
    {
        var textView = _editor.TextArea.TextView;
        var documentPosition = textView.GetVisualPosition(_editor.TextArea.Caret.Position, VisualYPosition.LineBottom);
        var viewportPosition = documentPosition - textView.ScrollOffset;
        _toolTip.PlacementTarget = textView;
        _toolTip.Placement = PlacementMode.RelativePoint;
        _toolTip.HorizontalOffset = Math.Max(0, viewportPosition.X);
        _toolTip.VerticalOffset = Math.Max(0, viewportPosition.Y + 4);
    }

    private void CancelPending()
    {
        var cancellation = Interlocked.Exchange(ref _requestCancellation, null);
        cancellation?.Cancel();
    }
}
