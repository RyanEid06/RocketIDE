using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using RocketIDE.Core.Search;

namespace RocketIDE.App.ViewModels;

public sealed class SearchResultViewModel(SearchMatch match)
{
    public SearchMatch Match { get; } = match ?? throw new ArgumentNullException(nameof(match));

    public string FilePath => Match.FilePath;

    public string Location => $"{Match.Line}:{Match.Column}";

    public string Preview => Match.Preview;
}

public sealed class SearchViewModel : INotifyPropertyChanged
{
    private readonly IWorkspaceSearchService _searchService;
    private string _rootPath = string.Empty;
    private string _pattern = string.Empty;
    private string _replacement = string.Empty;
    private bool _caseSensitive;
    private bool _wholeWord;
    private bool _useRegex;
    private bool _isSearching;
    private string _statusText = "Enter a pattern to search the workspace.";
    private ReplacePreview? _replacePreview;

    public SearchViewModel(IWorkspaceSearchService searchService)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<SearchMatch>? MatchActivated;

    public Func<IReadOnlyDictionary<string, string>>? OpenBufferProvider { get; set; }

    public ObservableCollection<SearchResultViewModel> Results { get; } = new();

    public string RootPath
    {
        get => _rootPath;
        set => SetField(ref _rootPath, value);
    }

    public string Pattern
    {
        get => _pattern;
        set
        {
            if (SetField(ref _pattern, value))
            {
                ClearPreview();
            }
        }
    }

    public string Replacement
    {
        get => _replacement;
        set
        {
            if (SetField(ref _replacement, value))
            {
                ClearPreview();
            }
        }
    }

    public bool CaseSensitive
    {
        get => _caseSensitive;
        set => SetField(ref _caseSensitive, value);
    }

    public bool WholeWord
    {
        get => _wholeWord;
        set => SetField(ref _wholeWord, value);
    }

    public bool UseRegex
    {
        get => _useRegex;
        set => SetField(ref _useRegex, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetField(ref _isSearching, value))
            {
                OnPropertyChanged(nameof(CanSearch));
                OnPropertyChanged(nameof(CanApplyReplace));
            }
        }
    }

    public bool CanSearch => !IsSearching && !string.IsNullOrWhiteSpace(RootPath) && !string.IsNullOrWhiteSpace(Pattern);

    public bool CanApplyReplace => !IsSearching && _replacePreview is not null;

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public ReplacePreview? ReplacePreview => _replacePreview;

    public async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (!CanSearch)
        {
            StatusText = "Choose a workspace and enter a search pattern.";
            return;
        }

        Results.Clear();
        ClearPreview();
        IsSearching = true;
        StatusText = "Searching…";
        try
        {
            IProgress<SearchResultBatch> progress = SynchronizationContext.Current is null
                ? new ImmediateProgress<SearchResultBatch>(AppendBatch)
                : new Progress<SearchResultBatch>(AppendBatch);
            var summary = await _searchService.SearchAsync(BuildQuery(), progress, cancellationToken).ConfigureAwait(true);
            StatusText = FormatSummary(summary);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = $"Search cancelled · {Results.Count} matches shown";
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or RegexParseException)
        {
            StatusText = $"Search failed: {exception.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task PreviewReplaceAsync(CancellationToken cancellationToken)
    {
        if (!CanSearch)
        {
            StatusText = "Choose a workspace and enter a search pattern before previewing replacements.";
            return;
        }

        IsSearching = true;
        StatusText = "Creating replace preview…";
        try
        {
            _replacePreview = await _searchService.CreateReplacePreviewAsync(BuildQuery(), Replacement, cancellationToken).ConfigureAwait(true);
            OnPropertyChanged(nameof(ReplacePreview));
            OnPropertyChanged(nameof(CanApplyReplace));
            StatusText = $"Preview: {_replacePreview.TotalMatches} matches in {_replacePreview.Files.Count} files";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Replace preview cancelled.";
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or RegexParseException)
        {
            StatusText = $"Replace preview failed: {exception.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task<ReplaceApplyResult?> ApplyReplaceAsync(CancellationToken cancellationToken)
    {
        if (_replacePreview is null)
        {
            StatusText = "Create a replace preview before applying changes.";
            return null;
        }

        IsSearching = true;
        try
        {
            var result = await _searchService.ApplyReplaceAsync(_replacePreview, cancellationToken).ConfigureAwait(true);
            _replacePreview = null;
            OnPropertyChanged(nameof(ReplacePreview));
            OnPropertyChanged(nameof(CanApplyReplace));
            StatusText = $"{result.MatchesReplaced} match{(result.MatchesReplaced == 1 ? string.Empty : "es")} replaced in {result.FilesChanged} file{(result.FilesChanged == 1 ? string.Empty : "s")}.";
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Replace cancelled.";
            return null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            StatusText = $"Replace not applied: {exception.Message}";
            return null;
        }
        finally
        {
            IsSearching = false;
        }
    }

    public void Activate(SearchResultViewModel result)
    {
        ArgumentNullException.ThrowIfNull(result);
        MatchActivated?.Invoke(this, result.Match);
    }

    private SearchQuery BuildQuery() => new(
        RootPath,
        Pattern,
        CaseSensitive,
        WholeWord,
        UseRegex,
        inMemoryBuffers: OpenBufferProvider?.Invoke());

    private void AppendBatch(SearchResultBatch batch)
    {
        foreach (var match in batch.Matches)
        {
            Results.Add(new SearchResultViewModel(match));
        }
        StatusText = $"{Results.Count} match{(Results.Count == 1 ? string.Empty : "es")} · {batch.FilesScanned} file{(batch.FilesScanned == 1 ? string.Empty : "s")} scanned";
    }

    private static string FormatSummary(SearchSummary summary) =>
        $"{summary.TotalMatches} match{(summary.TotalMatches == 1 ? string.Empty : "es")} in {summary.FilesScanned} file{(summary.FilesScanned == 1 ? string.Empty : "s")}" +
        (summary.ReachedResultLimit ? " · result limit reached" : string.Empty);

    private void ClearPreview()
    {
        if (_replacePreview is null)
        {
            return;
        }

        _replacePreview = null;
        OnPropertyChanged(nameof(ReplacePreview));
        OnPropertyChanged(nameof(CanApplyReplace));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(RootPath) or nameof(Pattern))
        {
            OnPropertyChanged(nameof(CanSearch));
        }
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class ImmediateProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}

public sealed class RegexParseException(string message, Exception innerException) : ArgumentException(message, innerException)
{
}
