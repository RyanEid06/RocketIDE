using System.IO;

namespace RocketIDE.App.Editor;

public sealed class RecentEditorHistory
{
    private readonly int _capacity;
    private readonly LinkedList<string> _paths = new();

    public RecentEditorHistory(int capacity = 64)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        _capacity = capacity;
    }

    public IReadOnlyList<string> Snapshot => _paths.ToArray();

    public void Record(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path);
        var node = _paths.First;
        while (node is not null)
        {
            var next = node.Next;
            if (PathComparer.Equals(node.Value, normalized))
            {
                _paths.Remove(node);
            }
            node = next;
        }

        _paths.AddFirst(normalized);
        while (_paths.Count > _capacity)
        {
            _paths.RemoveLast();
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
