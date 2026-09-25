using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.App.Commands;
using RocketIDE.App.Integration;

namespace RocketIDE.App.ViewModels;

public sealed record CommandPaletteItem(RocketCommandDefinition Definition, bool IsEnabled, int Score)
{
    public string DisplayName => Definition.DisplayName;
    public string Gesture => Definition.Gesture;
    public string Category => Definition.Category;
}

public sealed class CommandPaletteViewModel : INotifyPropertyChanged
{
    private const int MaxResults = 100;
    private readonly RocketCommandRegistry _registry;
    private readonly Func<string, RocketCommandState> _stateProvider;
    private string _query = string.Empty;
    private CommandPaletteItem? _selectedItem;

    public CommandPaletteViewModel(RocketCommandRegistry registry, Func<string, RocketCommandState> stateProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<CommandPaletteItem> Items { get; } = new();

    public string Query
    {
        get => _query;
        set
        {
            if (string.Equals(_query, value, StringComparison.Ordinal)) return;
            _query = value ?? string.Empty;
            OnPropertyChanged();
            Refresh();
        }
    }

    public CommandPaletteItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value)) return;
            _selectedItem = value;
            OnPropertyChanged();
        }
    }

    public string StatusText => Items.Count == 0 ? "No matching commands" : $"{Items.Count} command(s)";

    public void Refresh()
    {
        var query = Query.Trim();
        var matches = _registry.Definitions
            .Where(definition => !string.Equals(definition.Id, RocketCommandRegistry.CommandPalette, StringComparison.Ordinal))
            .Select(definition =>
            {
                var candidate = $"{definition.DisplayName} {definition.Id} {definition.Category}";
                var score = query.Length == 0 ? 0 : FuzzyMatcher.Score(query, candidate);
                return new CommandPaletteItem(definition, _stateProvider(definition.Id).IsEnabled, score);
            })
            .Where(item => query.Length == 0 || item.Score != int.MinValue)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .ToArray();

        Items.Clear();
        foreach (var item in matches) Items.Add(item);
        SelectedItem = Items.FirstOrDefault();
        OnPropertyChanged(nameof(StatusText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
