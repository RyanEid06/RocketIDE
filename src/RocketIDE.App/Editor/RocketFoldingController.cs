using System.ComponentModel;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Editor;

public readonly record struct EditorFoldingRange(int StartLine, int EndLine);

public interface IEditorFoldingRangeProvider
{
    Task<IReadOnlyList<EditorFoldingRange>?> RequestRangesAsync(
        string path,
        int documentVersion,
        CancellationToken cancellationToken);
}

public sealed class RocketFoldingController : IDisposable
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(150);
    private readonly TextEditor _editor;
    private readonly IEditorFoldingRangeProvider _provider;
    private FoldingManager? _foldingManager;
    private CancellationTokenSource? _requestCancellation;
    private DocumentTabViewModel? _document;
    private IReadOnlyList<EditorFoldingRange> _currentRanges = [];
    private IReadOnlyList<EditorFoldingRange> _pendingRestore = [];
    private bool _disposed;

    public RocketFoldingController(TextEditor editor, IEditorFoldingRangeProvider provider)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public void Attach(DocumentTabViewModel document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        Detach();
        if (!ReferenceEquals(_editor.Document, document.EditorDocument))
        {
            throw new InvalidOperationException("The folding controller must attach after the editor is bound to the view's logical TextDocument.");
        }

        _foldingManager = FoldingManager.Install(_editor.TextArea);
        _document = document;
        _document.PropertyChanged += Document_PropertyChanged;
        _ = RefreshSafelyAsync(TimeSpan.Zero);
    }

    public void Detach()
    {
        CancelPending();
        if (_document is not null)
        {
            _document.PropertyChanged -= Document_PropertyChanged;
            _document = null;
        }

        _currentRanges = [];
        _pendingRestore = [];
        var manager = Interlocked.Exchange(ref _foldingManager, null);
        if (manager is not null)
        {
            FoldingManager.Uninstall(manager);
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ScheduleRefreshAsync(TimeSpan.Zero, cancellationToken);
    }

    public IReadOnlyList<EditorFoldingRange> CaptureCollapsedState()
    {
        var manager = _foldingManager;
        if (manager is null || _currentRanges.Count == 0)
        {
            return [];
        }

        var foldings = manager.AllFoldings.ToArray();
        var result = new List<EditorFoldingRange>();
        foreach (var range in _currentRanges)
        {
            if (!TryGetOffsets(range, out var startOffset, out var endOffset))
            {
                continue;
            }

            var folding = foldings.FirstOrDefault(section =>
                section.StartOffset == startOffset && section.EndOffset == endOffset);
            if (folding?.IsFolded == true)
            {
                result.Add(range);
            }
        }
        return result;
    }

    public void RestoreCollapsedState(IEnumerable<EditorFoldingRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        _pendingRestore = ranges.Distinct().ToArray();
        ApplyCollapsedState(_pendingRestore);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Detach();
        GC.SuppressFinalize(this);
    }

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is DocumentTabViewModel document &&
            ReferenceEquals(document, _document) &&
            e.PropertyName == nameof(DocumentTabViewModel.Version))
        {
            _ = RefreshSafelyAsync(RefreshDelay);
        }
    }

    private async Task RefreshSafelyAsync(TimeSpan delay)
    {
        try
        {
            await ScheduleRefreshAsync(delay, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                ClearFoldings();
            }
        }
    }

    private Task ScheduleRefreshAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var document = _document;
        if (document is null || _foldingManager is null)
        {
            return Task.CompletedTask;
        }

        CancelPending();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _requestCancellation = linked;
        return RunRefreshAsync(document, document.Version, delay, linked);
    }

    private async Task RunRefreshAsync(
        DocumentTabViewModel document,
        int requestVersion,
        TimeSpan delay,
        CancellationTokenSource requestCancellation)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, requestCancellation.Token);
            }

            var ranges = await _provider.RequestRangesAsync(document.Path, requestVersion, requestCancellation.Token);
            if (requestCancellation.IsCancellationRequested ||
                !ReferenceEquals(document, _document) ||
                document.Version != requestVersion ||
                _foldingManager is null)
            {
                return;
            }

            var preservedState = CaptureCollapsedState();
            ApplyRanges(ranges ?? [], preservedState);
        }
        finally
        {
            if (ReferenceEquals(_requestCancellation, requestCancellation))
            {
                _requestCancellation = null;
            }
            requestCancellation.Dispose();
        }
    }

    private void ApplyRanges(
        IReadOnlyList<EditorFoldingRange> ranges,
        IReadOnlyList<EditorFoldingRange> preservedState)
    {
        var manager = _foldingManager;
        if (manager is null)
        {
            return;
        }

        var valid = ranges
            .Where(IsValidRange)
            .Distinct()
            .OrderBy(range => range.StartLine)
            .ThenBy(range => range.EndLine)
            .ToArray();
        var foldings = new List<NewFolding>(valid.Length);
        var accepted = new List<EditorFoldingRange>(valid.Length);
        foreach (var range in valid)
        {
            if (!TryGetOffsets(range, out var startOffset, out var endOffset))
            {
                continue;
            }
            foldings.Add(new NewFolding(startOffset, endOffset));
            accepted.Add(range);
        }

        manager.UpdateFoldings(foldings, -1);
        _currentRanges = accepted;
        _pendingRestore = preservedState.Concat(_pendingRestore).Distinct().ToArray();
        ApplyCollapsedState(_pendingRestore);
        _pendingRestore = [];
    }

    private bool IsValidRange(EditorFoldingRange range) =>
        range.StartLine >= 0 &&
        range.EndLine > range.StartLine &&
        range.EndLine < _editor.Document.LineCount;

    private void ApplyCollapsedState(IReadOnlyCollection<EditorFoldingRange> collapsed)
    {
        var manager = _foldingManager;
        if (manager is null || _currentRanges.Count == 0 || collapsed.Count == 0)
        {
            return;
        }

        var collapsedSet = collapsed.ToHashSet();
        var foldings = manager.AllFoldings.ToArray();
        foreach (var range in _currentRanges)
        {
            if (!TryGetOffsets(range, out var startOffset, out var endOffset))
            {
                continue;
            }

            var folding = foldings.FirstOrDefault(section =>
                section.StartOffset == startOffset && section.EndOffset == endOffset);
            if (folding is not null)
            {
                folding.IsFolded = collapsedSet.Contains(range);
            }
        }
    }

    private bool TryGetOffsets(EditorFoldingRange range, out int startOffset, out int endOffset)
    {
        startOffset = 0;
        endOffset = 0;
        if (!IsValidRange(range))
        {
            return false;
        }

        var startLine = _editor.Document.GetLineByNumber(range.StartLine + 1);
        var endLine = _editor.Document.GetLineByNumber(range.EndLine + 1);
        startOffset = startLine.EndOffset;
        endOffset = endLine.EndOffset;
        return endOffset > startOffset;
    }

    private void ClearFoldings()
    {
        _currentRanges = [];
        _pendingRestore = [];
        _foldingManager?.UpdateFoldings(Array.Empty<NewFolding>(), -1);
    }

    private void CancelPending()
    {
        var cancellation = Interlocked.Exchange(ref _requestCancellation, null);
        cancellation?.Cancel();
    }
}
