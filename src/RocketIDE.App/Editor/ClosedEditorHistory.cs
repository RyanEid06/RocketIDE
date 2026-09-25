using System.IO;

namespace RocketIDE.App.Editor;

public sealed class ClosedEditorHistory
{
    private readonly int _capacity;
    private readonly LinkedList<string> _paths = new();

    public ClosedEditorHistory(int capacity = 20)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        _capacity = capacity;
    }

    public int Count => _paths.Count;

    public IReadOnlyList<string> Snapshot => _paths.ToArray();

    public void Record(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path);
        var comparer = PathComparer;
        var existing = _paths.First;
        while (existing is not null)
        {
            var next = existing.Next;
            if (comparer.Equals(existing.Value, normalized))
            {
                _paths.Remove(existing);
            }
            existing = next;
        }

        _paths.AddFirst(normalized);
        while (_paths.Count > _capacity)
        {
            _paths.RemoveLast();
        }
    }

    public bool TryPop(out string path)
    {
        if (_paths.First is null)
        {
            path = string.Empty;
            return false;
        }

        path = _paths.First.Value;
        _paths.RemoveFirst();
        return true;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
