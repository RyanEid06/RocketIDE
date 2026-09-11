using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.Diagnostics;

namespace RocketIDE.App.ViewModels;

public sealed class ProblemItemViewModel
{
    public ProblemItemViewModel(RocketDiagnostic diagnostic)
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public RocketDiagnostic Diagnostic { get; }
    public DiagnosticSeverity Severity => Diagnostic.Severity;
    public string SeverityText => Severity.ToString();
    public string Code => Diagnostic.Code;
    public string Message => Diagnostic.Message;
    public string Source => Diagnostic.Source;
    public string SourceText => string.IsNullOrWhiteSpace(Source) ? "LSP" : $"LSP · {Source}";
    public string FilePath => Diagnostic.FilePath;
    public string FileName => Path.GetFileName(Diagnostic.FilePath);
    public int Line => Diagnostic.Range.StartLine + 1;
    public int Column => Diagnostic.Range.StartCharacter + 1;
    public SourceRange Range => Diagnostic.Range;
}

public sealed class ProblemsViewModel : INotifyPropertyChanged
{
    private readonly LiveDiagnosticsStore _store = new();
    private bool _showErrors = true;
    private bool _showWarnings = true;
    private bool _showInformation = true;
    private bool _showHints = true;
    private bool _groupByFile = true;
    private string _filterText = string.Empty;
    private string _statusText = "Live diagnostics unavailable — Rocket language server is offline.";
    private string _headerText = "PROBLEMS";

    public ProblemsViewModel()
    {
        ItemsView = new ListCollectionView(Items);
        ApplyGrouping();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;

    public ObservableCollection<ProblemItemViewModel> Items { get; } = new();
    public ListCollectionView ItemsView { get; }

    public bool ShowErrors
    {
        get => _showErrors;
        set => SetFilterField(ref _showErrors, value);
    }

    public bool ShowWarnings
    {
        get => _showWarnings;
        set => SetFilterField(ref _showWarnings, value);
    }

    public bool ShowInformation
    {
        get => _showInformation;
        set => SetFilterField(ref _showInformation, value);
    }

    public bool ShowHints
    {
        get => _showHints;
        set => SetFilterField(ref _showHints, value);
    }

    public bool GroupByFile
    {
        get => _groupByFile;
        set
        {
            if (_groupByFile == value)
            {
                return;
            }

            _groupByFile = value;
            OnPropertyChanged();
            ApplyGrouping();
            Rebuild();
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_filterText, value, StringComparison.Ordinal))
            {
                return;
            }

            _filterText = value;
            OnPropertyChanged();
            Rebuild();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string HeaderText
    {
        get => _headerText;
        private set => SetField(ref _headerText, value);
    }

    public void BeginSession(long generation, bool isOnline)
    {
        _store.BeginSession(generation, isOnline);
        Rebuild();
    }

    public void TrackDocument(string path, int version, bool isSupported)
    {
        _store.TrackDocument(path, version, isSupported);
        Rebuild();
    }

    public void RemoveDocument(string path)
    {
        _store.UntrackDocument(path);
        Rebuild();
    }

    public void ApplyPublication(RocketDiagnosticPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        _store.Publish(publication.Generation, publication.DocumentUri, publication.FilePath, publication.Version, publication.Diagnostics);
        Rebuild();
    }

    public LiveDiagnosticDocumentState GetDocumentState(string path) =>
        _store.GetSnapshot(path)?.State ?? (_store.IsOnline ? LiveDiagnosticDocumentState.Awaiting : LiveDiagnosticDocumentState.Offline);

    public IReadOnlyList<RocketDiagnostic> GetCurrentDiagnostics(string path)
    {
        var snapshot = _store.GetSnapshot(path);
        return snapshot?.State == LiveDiagnosticDocumentState.Current ? snapshot.Diagnostics : [];
    }

    private void Rebuild()
    {
        var snapshots = _store.Snapshots;
        var allCurrent = snapshots
            .Where(snapshot => snapshot.State == LiveDiagnosticDocumentState.Current)
            .SelectMany(snapshot => snapshot.Diagnostics)
            .ToArray();

        IEnumerable<RocketDiagnostic> filtered = allCurrent.Where(IsSeverityVisible);
        var filter = _filterText.Trim();
        if (filter.Length > 0)
        {
            filtered = filtered.Where(diagnostic => MatchesFilter(diagnostic, filter));
        }

        filtered = _groupByFile
            ? filtered.OrderBy(item => item.FilePath, PathComparer).ThenBy(item => item.Range.StartLine).ThenBy(item => item.Range.StartCharacter).ThenBy(item => item.Severity)
            : filtered.OrderBy(item => item.Severity).ThenBy(item => item.FilePath, PathComparer).ThenBy(item => item.Range.StartLine).ThenBy(item => item.Range.StartCharacter);

        Items.Clear();
        foreach (var diagnostic in filtered)
        {
            Items.Add(new ProblemItemViewModel(diagnostic));
        }

        HeaderText = allCurrent.Length == 0 ? "PROBLEMS" : $"PROBLEMS ({allCurrent.Length})";
        StatusText = BuildStatusText(snapshots, allCurrent.Length);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string BuildStatusText(IReadOnlyCollection<LiveDiagnosticDocumentSnapshot> snapshots, int problemCount)
    {
        if (!_store.IsOnline)
        {
            return "Live diagnostics unavailable — Rocket language server is offline.";
        }

        var unsupported = snapshots.Count(item => item.State == LiveDiagnosticDocumentState.Unsupported);
        var updating = snapshots.Any(item => item.State is LiveDiagnosticDocumentState.Awaiting or LiveDiagnosticDocumentState.Stale);
        if (updating)
        {
            return problemCount == 0 ? "Live diagnostics updating…" : $"{FormatProblemCount(problemCount)} · live diagnostics updating…";
        }

        if (unsupported > 0)
        {
            return problemCount == 0
                ? "Live diagnostics unavailable for file(s) over Rocket's 4 MiB limit."
                : $"{FormatProblemCount(problemCount)} · some files exceed Rocket's 4 MiB diagnostics limit.";
        }

        return problemCount == 0 ? "No problems detected." : FormatProblemCount(problemCount);
    }

    private bool IsSeverityVisible(RocketDiagnostic diagnostic) => diagnostic.Severity switch
    {
        DiagnosticSeverity.Error => _showErrors,
        DiagnosticSeverity.Warning => _showWarnings,
        DiagnosticSeverity.Information => _showInformation,
        DiagnosticSeverity.Hint => _showHints,
        _ => true,
    };

    private static bool MatchesFilter(RocketDiagnostic diagnostic, string filter) =>
        diagnostic.Code.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        diagnostic.Message.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        diagnostic.Source.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        diagnostic.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(diagnostic.FilePath).Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string FormatProblemCount(int count) => count == 1 ? "1 problem" : $"{count} problems";

    private void ApplyGrouping()
    {
        using var defer = ItemsView.DeferRefresh();
        ItemsView.GroupDescriptions.Clear();
        if (_groupByFile)
        {
            ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProblemItemViewModel.FilePath)));
        }
    }

    private void SetFilterField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        Rebuild();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
