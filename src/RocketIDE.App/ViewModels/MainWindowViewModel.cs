using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using RocketIDE.App.Commands;
using RocketIDE.App.ViewModels.Explorer;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Core.Documents;
using RocketIDE.Core.Search;
using RocketIDE.Core.Workspaces;
using RocketIDE.Infrastructure.Files;
using RocketIDE.Rocket.Diagnostics;
using RocketIDE.Debugger;

namespace RocketIDE.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private DocumentTabViewModel? _activeDocument;
    private string _rocketSdkStatus = "Rocket SDK: not configured";
    private string _lspStatus = "LSP: offline";
    private string _activeTargetStatus = "Target: none";
    private string _caretStatus = "Ln 1, Col 1";
    private string _encodingStatus = "UTF-8";
    private bool _rocketCommandRunning;
    private bool _hasRocketCommandTarget;
    private bool _rocketRunAvailable;

    public MainWindowViewModel(IWorkspaceFileSystem workspaceFileSystem, IWorkspaceSearchService? searchService = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceFileSystem);
        Explorer = new WorkspaceExplorerViewModel(workspaceFileSystem);
        Problems = new ProblemsViewModel();
        References = new ReferencesViewModel();
        Output = new OutputViewModel();
        Tests = new TestsViewModel();
        Debug = new DebugViewModel();
        Debug.PropertyChanged += (_, _) => RaiseRocketCommandProperties();
        Search = new SearchViewModel(searchService ?? new WorkspaceSearchService())
        {
            OpenBufferProvider = GetOpenBufferTexts,
        };
        CommandRegistry = new RocketCommandRegistry();
        Explorer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceExplorerViewModel.HasWorkspace))
            {
                OnPropertyChanged(nameof(HasWorkspace));
                RaiseRocketCommandProperties();
            }
        };
        Problems.Changed += Problems_Changed;
        Documents.CollectionChanged += Documents_CollectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DocumentTabViewModel> Documents { get; } = new();

    public WorkspaceExplorerViewModel Explorer { get; }

    public ProblemsViewModel Problems { get; }

    public ReferencesViewModel References { get; }

    public OutputViewModel Output { get; }

    public TestsViewModel Tests { get; }

    public DebugViewModel Debug { get; }

    public SearchViewModel Search { get; }

    public RocketCommandRegistry CommandRegistry { get; }

    public RocketCommandState GetCommandState(string commandId) =>
        CommandRegistry.Evaluate(commandId, new RocketCommandContext(
            HasDocuments,
            _hasRocketCommandTarget,
            _rocketRunAvailable,
            _rocketCommandRunning,
            HasWorkspace,
            Debug.IsActive,
            Debug.IsRunning,
            Debug.IsStopped));

    public ObservableCollection<string> RecentWorkspaces { get; } = new();


    public bool HasWorkspace => Explorer.HasWorkspace;

    public bool HasDocuments => Documents.Count > 0;

    public DocumentTabViewModel? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (ReferenceEquals(_activeDocument, value))
            {
                return;
            }

            if (_activeDocument is not null)
            {
                _activeDocument.CaretChanged -= ActiveDocument_CaretChanged;
            }

            _activeDocument = value;

            if (_activeDocument is not null)
            {
                _activeDocument.CaretChanged += ActiveDocument_CaretChanged;
            }

            UpdateCaretStatus();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle => ActiveDocument is null ? "RocketIDE" : $"{ActiveDocument.DisplayName} — RocketIDE";


    public bool CanRocketCheck => GetCommandState(RocketCommandRegistry.Check).IsEnabled;
    public bool CanRocketBuild => GetCommandState(RocketCommandRegistry.Build).IsEnabled;
    public bool CanRocketTest => GetCommandState(RocketCommandRegistry.Test).IsEnabled;
    public bool CanRocketRun => GetCommandState(RocketCommandRegistry.Run).IsEnabled;
    public bool CanRocketStop => GetCommandState(RocketCommandRegistry.Stop).IsEnabled;
    public bool CanRocketNewProject => GetCommandState(RocketCommandRegistry.NewProject).IsEnabled;
    public bool CanRocketAdvanced => GetCommandState(RocketCommandRegistry.Resolve).IsEnabled;
    public bool CanQuickOpen => GetCommandState(RocketCommandRegistry.QuickOpen).IsEnabled;
    public bool CanDebugStartContinue => GetCommandState(RocketCommandRegistry.DebugStartContinue).IsEnabled;
    public bool CanDebugPause => GetCommandState(RocketCommandRegistry.DebugPause).IsEnabled;
    public bool CanDebugStop => GetCommandState(RocketCommandRegistry.DebugStop).IsEnabled;
    public bool CanDebugToggleBreakpoint => GetCommandState(RocketCommandRegistry.DebugToggleBreakpoint).IsEnabled;
    public bool CanDebugStepOver => GetCommandState(RocketCommandRegistry.DebugStepOver).IsEnabled;
    public bool CanDebugStepInto => GetCommandState(RocketCommandRegistry.DebugStepInto).IsEnabled;
    public bool CanDebugStepOut => GetCommandState(RocketCommandRegistry.DebugStepOut).IsEnabled;

    public void SetRocketCommandAvailability(bool hasTarget, bool canRun)
    {
        if (_hasRocketCommandTarget == hasTarget && _rocketRunAvailable == canRun)
        {
            return;
        }
        _hasRocketCommandTarget = hasTarget;
        _rocketRunAvailable = canRun;
        RaiseRocketCommandProperties();
    }

    public void SetRocketCommandRunning(bool running)
    {
        if (_rocketCommandRunning == running)
        {
            return;
        }
        _rocketCommandRunning = running;
        RaiseRocketCommandProperties();
    }

    public string RocketSdkStatus
    {
        get => _rocketSdkStatus;
        set => SetField(ref _rocketSdkStatus, value);
    }

    public string LspStatus
    {
        get => _lspStatus;
        set => SetField(ref _lspStatus, value);
    }

    public string ActiveTargetStatus
    {
        get => _activeTargetStatus;
        set => SetField(ref _activeTargetStatus, value);
    }

    public string CaretStatus
    {
        get => _caretStatus;
        private set => SetField(ref _caretStatus, value);
    }

    public string EncodingStatus
    {
        get => _encodingStatus;
        set => SetField(ref _encodingStatus, value);
    }

    public DocumentTabViewModel AddOrActivate(IDocumentStore documentStore, DocumentSnapshot snapshot)
    {
        var existing = Documents.FirstOrDefault(document => document.Id == snapshot.Id);
        if (existing is not null)
        {
            ActiveDocument = existing;
            return existing;
        }

        var tab = new DocumentTabViewModel(documentStore, snapshot);
        Documents.Add(tab);
        ActiveDocument = tab;
        return tab;
    }

    public void ApplyDiagnosticPublication(RocketDiagnosticPublication publication) =>
        Problems.ApplyPublication(publication);

    public void BeginDiagnosticSession(long generation, bool isOnline) =>
        Problems.BeginSession(generation, isOnline);

    public void SetDocumentDiagnosticSupport(string path, int version, bool isSupported)
    {
        var fullPath = Path.GetFullPath(path);
        var document = Documents.FirstOrDefault(tab =>
            IsRocketPath(tab.Path) &&
            string.Equals(Path.GetFullPath(tab.Path), fullPath, PathComparison));
        if (document is null || document.Version != version)
        {
            return;
        }

        Problems.TrackDocument(document.Path, version, isSupported);
    }

    public void AppendOutput(string line) => Output.Append(line);

    public void SetRecentWorkspaces(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        RecentWorkspaces.Clear();
        foreach (var path in paths.Take(8))
        {
            RecentWorkspaces.Add(NormalizeWorkspacePath(path));
        }
    }

    public void AddRecentWorkspace(string path)
    {
        var fullPath = NormalizeWorkspacePath(path);
        var existing = RecentWorkspaces.FirstOrDefault(item => string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RecentWorkspaces.Remove(existing);
        }

        RecentWorkspaces.Insert(0, fullPath);
        while (RecentWorkspaces.Count > 8)
        {
            RecentWorkspaces.RemoveAt(RecentWorkspaces.Count - 1);
        }
    }

    public void Remove(DocumentTabViewModel tab)
    {
        var index = Documents.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var wasActive = ReferenceEquals(ActiveDocument, tab);
        Documents.RemoveAt(index);

        if (wasActive)
        {
            ActiveDocument = Documents.Count == 0
                ? null
                : Documents[Math.Min(index, Documents.Count - 1)];
        }
    }

    private void Documents_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DocumentTabViewModel tab in e.OldItems)
            {
                tab.PropertyChanged -= Document_DiagnosticsPropertyChanged;
                if (IsRocketPath(tab.Path))
                {
                    Problems.RemoveDocument(tab.Path);
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DocumentTabViewModel tab in e.NewItems)
            {
                tab.PropertyChanged += Document_DiagnosticsPropertyChanged;
                if (IsRocketPath(tab.Path))
                {
                    Problems.TrackDocument(tab.Path, tab.Version, isSupported: true);
                }
            }
        }

        OnPropertyChanged(nameof(HasDocuments));
        RaiseRocketCommandProperties();
    }

    private void Document_DiagnosticsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentTabViewModel tab ||
            !IsRocketPath(tab.Path) ||
            e.PropertyName != nameof(DocumentTabViewModel.Version))
        {
            return;
        }

        var isSupported = tab.DiagnosticState != LiveDiagnosticDocumentState.Unsupported;
        Problems.TrackDocument(tab.Path, tab.Version, isSupported);
    }

    private void Problems_Changed(object? sender, EventArgs e)
    {
        foreach (var tab in Documents.Where(tab => IsRocketPath(tab.Path)))
        {
            tab.SetDiagnostics(Problems.GetCurrentDiagnostics(tab.Path), Problems.GetDocumentState(tab.Path));
        }
    }

    private IReadOnlyDictionary<string, string> GetOpenBufferTexts() =>
        Documents
            .GroupBy(document => document.Path, PathComparer)
            .ToDictionary(group => group.Key, group => group.Last().Text, PathComparer);

    private static bool IsRocketPath(string path) =>
        string.Equals(Path.GetExtension(path), ".rocket", StringComparison.OrdinalIgnoreCase);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static IEqualityComparer<string> PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string NormalizeWorkspacePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void ActiveDocument_CaretChanged(object? sender, EventArgs e) => UpdateCaretStatus();

    private void UpdateCaretStatus()
    {
        CaretStatus = ActiveDocument is null
            ? "Ln 1, Col 1"
            : $"Ln {ActiveDocument.CaretLine}, Col {ActiveDocument.CaretColumn}";
    }

    private void RaiseRocketCommandProperties()
    {
        OnPropertyChanged(nameof(CanRocketCheck));
        OnPropertyChanged(nameof(CanRocketBuild));
        OnPropertyChanged(nameof(CanRocketRun));
        OnPropertyChanged(nameof(CanRocketTest));
        OnPropertyChanged(nameof(CanRocketStop));
        OnPropertyChanged(nameof(CanRocketNewProject));
        OnPropertyChanged(nameof(CanRocketAdvanced));
        OnPropertyChanged(nameof(CanQuickOpen));
        OnPropertyChanged(nameof(CanDebugStartContinue));
        OnPropertyChanged(nameof(CanDebugPause));
        OnPropertyChanged(nameof(CanDebugStop));
        OnPropertyChanged(nameof(CanDebugToggleBreakpoint));
        OnPropertyChanged(nameof(CanDebugStepOver));
        OnPropertyChanged(nameof(CanDebugStepInto));
        OnPropertyChanged(nameof(CanDebugStepOut));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
