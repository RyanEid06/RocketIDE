using System.ComponentModel;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;

namespace RocketIDE.App.Editor.SemanticTokens;

internal sealed class RocketSemanticTokenController : IDisposable
{
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromMilliseconds(180);
    private readonly TextEditor _editor;
    private readonly Func<IRocketEditorFeatureService?> _serviceProvider;
    private readonly SemanticTokenMarkerCollection _markers = new();
    private readonly SemanticTokenColorizer _colorizer;
    private CancellationTokenSource? _requestCancellation;
    private DocumentTabViewModel? _document;
    private bool _disposed;

    public RocketSemanticTokenController(TextEditor editor, Func<IRocketEditorFeatureService?> serviceProvider)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _colorizer = new SemanticTokenColorizer(_markers, key => _editor.TryFindResource(key) as Brush);
        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
    }

    public void Attach(DocumentTabViewModel document)
    {
        Detach();
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.PropertyChanged += Document_PropertyChanged;
        if (document.DiagnosticState != LiveDiagnosticDocumentState.Unsupported)
        {
            _ = ScheduleRefreshAsync(TimeSpan.Zero);
        }
    }

    public void Detach()
    {
        CancelPending();
        if (_document is not null)
        {
            _serviceProvider()?.InvalidateSemanticTokens(_document.Path);
            _document.PropertyChanged -= Document_PropertyChanged;
            _document = null;
        }
        ClearMarkers();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Detach();
        _editor.TextArea.TextView.LineTransformers.Remove(_colorizer);
    }

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentTabViewModel document || !ReferenceEquals(document, _document))
        {
            return;
        }

        if (e.PropertyName == nameof(DocumentTabViewModel.Version))
        {
            ClearMarkers();
            _ = ScheduleRefreshAsync(RefreshDebounce);
        }
        else if (e.PropertyName == nameof(DocumentTabViewModel.DiagnosticState))
        {
            if (document.DiagnosticState is LiveDiagnosticDocumentState.Offline or LiveDiagnosticDocumentState.Unsupported)
            {
                CancelPending();
                ClearMarkers();
            }
            else
            {
                _ = ScheduleRefreshAsync(RefreshDebounce);
            }
        }
    }

    private async Task ScheduleRefreshAsync(TimeSpan delay)
    {
        var document = _document;
        var service = _serviceProvider();
        if (document is null || service is null || document.DiagnosticState == LiveDiagnosticDocumentState.Unsupported)
        {
            return;
        }

        CancelPending();
        var cancellation = new CancellationTokenSource();
        _requestCancellation = cancellation;
        var requestVersion = document.Version;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellation.Token);
            }
            var result = await service.RequestSemanticTokensAsync(document.Path, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(document, _document) || requestVersion != document.Version)
            {
                return;
            }

            if (result is null)
            {
                ClearMarkers();
                return;
            }
            _markers.Update(_editor.Document, result.Tokens);
            _editor.TextArea.TextView.Redraw();
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

    private void ClearMarkers()
    {
        if (_markers.Markers.Count == 0)
        {
            return;
        }
        _markers.Clear();
        _editor.TextArea.TextView.Redraw();
    }

    private void CancelPending()
    {
        var cancellation = Interlocked.Exchange(ref _requestCancellation, null);
        cancellation?.Cancel();
    }
}
