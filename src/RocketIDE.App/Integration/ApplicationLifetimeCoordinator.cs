using System.Diagnostics;

namespace RocketIDE.App.Integration;

/// <summary>Owns the application's one shutdown clock and cancellation signal.</summary>
public sealed class ApplicationLifetimeCoordinator : IDisposable
{
    private readonly CancellationTokenSource _workCancellation = new();
    private readonly Stopwatch _shutdownClock = new();
    private readonly TimeSpan _normalDeadline;
    private readonly TimeSpan _hardDeadline;
    private readonly Action<string>? _reportFailure;
    private int _stopping;

    public ApplicationLifetimeCoordinator(
        TimeSpan? normalDeadline = null,
        TimeSpan? hardDeadline = null,
        Action<string>? reportFailure = null)
    {
        _normalDeadline = normalDeadline ?? TimeSpan.FromSeconds(2);
        _hardDeadline = hardDeadline ?? TimeSpan.FromSeconds(5);
        if (_normalDeadline <= TimeSpan.Zero || _hardDeadline < _normalDeadline)
        {
            throw new ArgumentOutOfRangeException(nameof(hardDeadline));
        }
        _reportFailure = reportFailure;
    }

    public CancellationToken WorkToken => _workCancellation.Token;
    public bool IsStopping => Volatile.Read(ref _stopping) != 0;
    public bool TryAcceptWork() => !IsStopping;

    public void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }
        _shutdownClock.Start();
        // CancelAsync marks the token cancelled immediately while invoking registrations
        // asynchronously. A stalled cancellation callback cannot delay the exit clock.
        _ = _workCancellation.CancelAsync();
    }

    public Task<bool> RunGracefulAsync(string name, Func<CancellationToken, Task> action) =>
        RunWithinAsync(name, action, _normalDeadline);

    public Task<bool> RunForcedAsync(string name, Func<CancellationToken, Task> action) =>
        RunWithinAsync(name, action, _hardDeadline);

    private async Task<bool> RunWithinAsync(string name, Func<CancellationToken, Task> action, TimeSpan deadline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        if (!IsStopping)
        {
            throw new InvalidOperationException("Shutdown has not begun.");
        }
        var remaining = deadline - _shutdownClock.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            _reportFailure?.Invoke($"{name} skipped: shutdown deadline exhausted.");
            return false;
        }

        var cancellation = new CancellationTokenSource(remaining);
        Task? task = null;
        try
        {
            task = action(cancellation.Token);
            await task.WaitAsync(remaining).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            _reportFailure?.Invoke($"{name} exceeded the shutdown budget: {exception.Message}");
            return false;
        }
        catch (Exception exception)
        {
            _reportFailure?.Invoke($"{name} shutdown failed: {exception.Message}");
            return false;
        }
        finally
        {
            if (task is { IsCompleted: false })
            {
                _ = task.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
            }
            else
            {
                cancellation.Dispose();
            }
        }
    }

    public void Dispose() => _workCancellation.Dispose();
}
