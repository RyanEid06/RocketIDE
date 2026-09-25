using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.App.Integration;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.ViewModels;

public sealed record SymbolSearchItem(
    string Name,
    string Detail,
    string KindText,
    string Path,
    SourceRange Range,
    int Score = 0);

public sealed class SymbolSearchViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxDisplayedResults = 200;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<SymbolSearchItem>>>? _remoteSearch;
    private readonly IReadOnlyList<SymbolSearchItem> _localItems;
    private CancellationTokenSource? _searchCancellation;
    private string _query = string.Empty;
    private string _statusText = string.Empty;
    private SymbolSearchItem? _selectedItem;
    private long _searchSerial;

    public SymbolSearchViewModel(IReadOnlyList<SymbolSearchItem> localItems)
    {
        _localItems = localItems ?? throw new ArgumentNullException(nameof(localItems));
        ApplyLocalFilter();
    }

    public SymbolSearchViewModel(Func<string, CancellationToken, Task<IReadOnlyList<SymbolSearchItem>>> remoteSearch)
    {
        _remoteSearch = remoteSearch ?? throw new ArgumentNullException(nameof(remoteSearch));
        _localItems = [];
        _ = SearchRemoteAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<SymbolSearchItem> Items { get; } = new();

    public string Query
    {
        get => _query;
        set
        {
            if (string.Equals(_query, value, StringComparison.Ordinal)) return;
            _query = value ?? string.Empty;
            OnPropertyChanged();
            if (_remoteSearch is null) ApplyLocalFilter(); else _ = SearchRemoteAsync();
        }
    }

    public SymbolSearchItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value)) return;
            _selectedItem = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (string.Equals(_statusText, value, StringComparison.Ordinal)) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public static IReadOnlyList<SymbolSearchItem> FlattenDocumentSymbols(IReadOnlyList<RocketDocumentSymbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var result = new List<SymbolSearchItem>();
        foreach (var symbol in symbols) Add(symbol, null, result);
        return result;

        static void Add(RocketDocumentSymbol symbol, string? parent, List<SymbolSearchItem> output)
        {
            var detail = string.IsNullOrWhiteSpace(parent) ? symbol.Detail ?? string.Empty : parent;
            output.Add(new SymbolSearchItem(
                symbol.Name,
                detail,
                SymbolKindNames.GetName(symbol.Kind),
                symbol.Path,
                new SourceRange(
                    symbol.SelectionRange.Start.Line,
                    symbol.SelectionRange.Start.Character,
                    symbol.SelectionRange.End.Line,
                    symbol.SelectionRange.End.Character)));
            var nextParent = string.IsNullOrWhiteSpace(parent) ? symbol.Name : $"{parent} › {symbol.Name}";
            foreach (var child in symbol.Children) Add(child, nextParent, output);
        }
    }

    public static SymbolSearchItem FromWorkspaceSymbol(RocketWorkspaceSymbol symbol, string? workspaceRoot)
    {
        var detail = symbol.ContainerName ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            try { detail = Path.GetRelativePath(workspaceRoot, symbol.Path); } catch (ArgumentException) { }
        }
        return new SymbolSearchItem(
            symbol.Name,
            detail,
            SymbolKindNames.GetName(symbol.Kind),
            symbol.Path,
            new SourceRange(symbol.Range.Start.Line, symbol.Range.Start.Character, symbol.Range.End.Line, symbol.Range.End.Character));
    }

    private void ApplyLocalFilter()
    {
        var query = Query.Trim();
        var matches = _localItems
            .Select(item => item with { Score = query.Length == 0 ? 0 : FuzzyMatcher.Score(query, $"{item.Name} {item.Detail} {item.KindText}") })
            .Where(item => query.Length == 0 || item.Score != int.MinValue)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxDisplayedResults)
            .ToArray();
        ReplaceItems(matches);
    }

    private async Task SearchRemoteAsync()
    {
        var serial = Interlocked.Increment(ref _searchSerial);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        try
        {
            await Task.Delay(150, cancellation.Token);
            StatusText = "Searching rocket-lsp…";
            var results = await _remoteSearch!(Query.Trim(), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (serial != Volatile.Read(ref _searchSerial)) return;
            var query = Query.Trim();
            var ranked = results
                .Select(item => item with { Score = query.Length == 0 ? 0 : FuzzyMatcher.Score(query, $"{item.Name} {item.Detail} {item.KindText}") })
                .Where(item => query.Length == 0 || item.Score != int.MinValue)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxDisplayedResults)
                .ToArray();
            ReplaceItems(ranked);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or LspProtocolException or JsonRpcResponseException)
        {
            if (serial == Volatile.Read(ref _searchSerial))
            {
                Items.Clear();
                SelectedItem = null;
                StatusText = $"Symbol search failed: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _searchCancellation, null, cancellation), cancellation))
                cancellation.Dispose();
        }
    }

    private void ReplaceItems(IEnumerable<SymbolSearchItem> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        SelectedItem = Items.FirstOrDefault();
        StatusText = Items.Count == 0 ? "No matching symbols" : $"{Items.Count} symbol(s)";
    }

    public void Dispose()
    {
        var cancellation = Interlocked.Exchange(ref _searchCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
