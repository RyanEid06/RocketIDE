using System.IO;

namespace RocketIDE.App.Integration;

public sealed class DocumentChangeScheduler : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<RocketSessionDocument, CancellationToken, Task> _changeAsync;
    private readonly Action<Exception>? _onError;
    private readonly TimeSpan _delay;
    private readonly Dictionary<string, PendingChange> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _activeTasks = [];
    private bool _disposed;

    public DocumentChangeScheduler(
        Func<RocketSessionDocument, CancellationToken, Task> changeAsync,
        TimeSpan? delay = null,
        Action<Exception>? onError = null)
    {
        _changeAsync = changeAsync ?? throw new ArgumentNullException(nameof(changeAsync));
        _delay = delay ?? TimeSpan.FromMilliseconds(75);
        if (_delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }
        _onError = onError;
    }

    public void Schedule(RocketSessionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = NormalizePath(document.Path);
        PendingChange pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending.Remove(path, out var previous))
            {
                previous.Cancellation.Cancel();
            }

            pending = new PendingChange(document, new CancellationTokenSource());
            _pending[path] = pending;
            var task = RunAsync(path, pending);
            _activeTasks.Add(task);
        }
    }

    public void Cancel(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            if (_pending.Remove(NormalizePath(path), out var pending))
            {
                pending.Cancellation.Cancel();
            }
        }
    }

    public async Task WaitForIdleAsync()
    {
        while (true)
        {
            Task[] tasks;
            lock (_gate)
            {
                _activeTasks.RemoveWhere(task => task.IsCompleted);
                tasks = _activeTasks.ToArray();
            }

            if (tasks.Length == 0)
            {
                return;
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var pending in _pending.Values)
            {
                pending.Cancellation.Cancel();
            }
            _pending.Clear();
        }

        await WaitForIdleAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(string path, PendingChange pending)
    {
        try
        {
            await Task.Delay(_delay, pending.Cancellation.Token).ConfigureAwait(false);
            await _changeAsync(pending.Document, pending.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pending.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _onError?.Invoke(exception);
        }
        finally
        {
            lock (_gate)
            {
                if (_pending.TryGetValue(path, out var current) && ReferenceEquals(current, pending))
                {
                    _pending.Remove(path);
                }
            }
            pending.Cancellation.Dispose();
        }
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private sealed class PendingChange(RocketSessionDocument document, CancellationTokenSource cancellation)
    {
        public RocketSessionDocument Document { get; } = document;

        public CancellationTokenSource Cancellation { get; } = cancellation;

    }
}
