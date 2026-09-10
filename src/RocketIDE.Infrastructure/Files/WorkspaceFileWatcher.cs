using RocketIDE.Core.Workspaces;

namespace RocketIDE.Infrastructure.Files;

public sealed class WorkspaceChangesEventArgs(IReadOnlyList<WorkspaceChange> changes) : EventArgs
{
    public IReadOnlyList<WorkspaceChange> Changes { get; } = changes;
}

public sealed class WorkspaceFileWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly FileSystemWatcher _watcher;
    private readonly Dictionary<string, WorkspaceChange> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private readonly TimeSpan _debounce;
    private bool _disposed;

    public WorkspaceFileWatcher(string rootPath, TimeSpan? debounce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Workspace directory '{fullPath}' does not exist.");
        }

        RootPath = fullPath;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(180);
        _watcher = new FileSystemWatcher(fullPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = false,
        };
        _watcher.Created += (_, e) => Queue(new WorkspaceChange(WorkspaceChangeKind.Created, e.FullPath));
        _watcher.Changed += (_, e) => Queue(new WorkspaceChange(WorkspaceChangeKind.Changed, e.FullPath));
        _watcher.Deleted += (_, e) => Queue(new WorkspaceChange(WorkspaceChangeKind.Deleted, e.FullPath));
        _watcher.Renamed += (_, e) => Queue(new WorkspaceChange(WorkspaceChangeKind.Renamed, e.FullPath, e.OldFullPath));
        _watcher.Error += (_, _) => Queue(new WorkspaceChange(WorkspaceChangeKind.Created, RootPath));
        _timer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<WorkspaceChangesEventArgs>? ChangesAvailable;

    public string RootPath { get; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        lock (_gate)
        {
            _pending.Clear();
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void Queue(WorkspaceChange change)
    {
        if (_disposed || IsExcluded(change.Path))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var key = change.Kind == WorkspaceChangeKind.Renamed
                ? $"rename|{change.OldPath}|{change.Path}"
                : $"{change.Kind}|{change.Path}";
            _pending[key] = change with { Path = Path.GetFullPath(change.Path), OldPath = change.OldPath is null ? null : Path.GetFullPath(change.OldPath) };
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        WorkspaceChange[] changes;
        lock (_gate)
        {
            if (_disposed || _pending.Count == 0)
            {
                return;
            }

            changes = _pending.Values.ToArray();
            _pending.Clear();
        }

        ChangesAvailable?.Invoke(this, new WorkspaceChangesEventArgs(changes));
    }

    private bool IsExcluded(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith(".", StringComparison.Ordinal) &&
            fileName.Contains(".rocketide-", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var relative = Path.GetRelativePath(RootPath, path);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return true;
        }

        return relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(WorkspaceFileSystem.ShouldHideDirectory);
    }
}
