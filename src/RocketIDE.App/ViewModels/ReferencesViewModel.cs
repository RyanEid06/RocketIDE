using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.ViewModels;

public sealed record ReferenceItemViewModel(string FilePath, LspRange Range)
{
    public string FileName => Path.GetFileName(FilePath);
    public int Line => Range.Start.Line + 1;
    public int Column => Range.Start.Character + 1;
    public string LocationText => $"{FilePath}:{Line}:{Column}";
}

public sealed class ReferencesViewModel : INotifyPropertyChanged
{
    private string _label = "References";

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ReferenceItemViewModel> Items { get; } = new();
    public string HeaderText => $"{_label.ToUpperInvariant()} ({Items.Count})";
    public string StatusText => Items.Count == 0 ? $"No {_label.ToLowerInvariant()} from rocket-lsp." : $"{Items.Count} server-provided {_label.ToLowerInvariant()}.";

    public void SetResults(string label, IEnumerable<RocketLocation> locations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(locations);
        _label = label;
        Items.Clear();
        foreach (var location in locations)
        {
            Items.Add(new ReferenceItemViewModel(location.Path, location.Range));
        }
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(StatusText));
    }

    public void Clear() => SetResults("References", []);

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
