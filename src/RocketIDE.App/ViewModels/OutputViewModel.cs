using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

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

    public ObservableCollection<string> Lines { get; } = new OutputLineCollection();

    public void Append(string? line)
    {
        if (line is null)
        {
            return;
        }
        AppendMany([line]);
    }

    public void AppendMany(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ((OutputLineCollection)Lines).AppendMany(lines, _maxLines);
    }

    public void BeginCommand(string name, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        Append($"=== Rocket {name}: {target} ===");
    }

    public void Clear() => Lines.Clear();

    private sealed class OutputLineCollection : ObservableCollection<string>
    {
        public void AppendMany(IEnumerable<string> lines, int maxLines)
        {
            var added = false;
            foreach (var line in lines)
            {
                if (line is null) continue;
                Items.Add(line);
                added = true;
            }
            if (!added) return;
            var excess = Items.Count - maxLines;
            if (excess > 0 && Items is List<string> storage)
            {
                storage.RemoveRange(0, excess);
            }
            else
            {
                for (var index = 0; index < excess; index++) Items.RemoveAt(0);
            }
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
