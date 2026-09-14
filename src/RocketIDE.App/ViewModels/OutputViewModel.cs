using System.Collections.ObjectModel;

namespace RocketIDE.App.ViewModels;

public sealed class OutputViewModel
{
    private readonly int _maxLines;

    public OutputViewModel(int maxLines = 4000)
    {
        if (maxLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLines));
        }
        _maxLines = maxLines;
    }

    public ObservableCollection<string> Lines { get; } = new();

    public void Append(string? line)
    {
        if (line is null)
        {
            return;
        }
        Lines.Add(line);
        while (Lines.Count > _maxLines)
        {
            Lines.RemoveAt(0);
        }
    }

    public void BeginCommand(string name, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        Append($"=== Rocket {name}: {target} ===");
    }

    public void Clear() => Lines.Clear();
}
