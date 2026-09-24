namespace RocketIDE.App.ViewModels;

/// <summary>Queues producer output and schedules a bounded dispatcher batch.</summary>
public sealed class UiOutputBuffer
{
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private readonly OutputViewModel _output;
    private readonly Action<Action> _schedule;
    private readonly int _maxPending;
    private readonly int _batchSize;
    private TaskCompletionSource? _drained;
    private bool _scheduled;
    private bool _completed;

    public UiOutputBuffer(OutputViewModel output, Action<Action> schedule, int maxPending = 4096, int batchSize = 256)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        if (maxPending <= 0 || batchSize <= 0 || batchSize > maxPending)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
        _maxPending = maxPending;
        _batchSize = batchSize;
    }

    public void Enqueue(string? line)
    {
        if (line is null) return;
        var schedule = false;
        lock (_gate)
        {
            if (_completed) return;
            _pending.Enqueue(line);
            while (_pending.Count > _maxPending)
            {
                _pending.Dequeue();
            }
            if (!_scheduled)
            {
                _scheduled = true;
                schedule = true;
            }
        }
        if (schedule) _schedule(Drain);
    }

    public void BeginCommand(string name, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        Enqueue($"=== Rocket {name}: {target} ===");
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
            _output.Clear();
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_pending.Count == 0 && !_scheduled) return Task.CompletedTask;
            _drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _drained.Task.WaitAsync(cancellationToken);
        }
    }

    public void Complete()
    {
        lock (_gate) _completed = true;
    }

    private void Drain()
    {
        var batch = new List<string>(_batchSize);
        lock (_gate)
        {
            while (batch.Count < _batchSize && _pending.TryDequeue(out var line))
            {
                batch.Add(line);
            }
        }
        _output.AppendMany(batch);

        var schedule = false;
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            if (_pending.Count > 0)
            {
                schedule = true;
            }
            else
            {
                _scheduled = false;
                drained = _drained;
                _drained = null;
            }
        }
        drained?.TrySetResult();
        if (schedule) _schedule(Drain);
    }
}
