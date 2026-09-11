using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using RocketIDE.App.ViewModels.Explorer;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Core.Documents;
using RocketIDE.Core.Workspaces;
using RocketIDE.Rocket.Diagnostics;

namespace RocketIDE.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private DocumentTabViewModel? _activeDocument;
    private string _rocketSdkStatus = "Rocket SDK: not configured";
    private string _lspStatus = "LSP: offline";
    private string _activeTargetStatus = "Target: none";
    private string _caretStatus = "Ln 1, Col 1";
    private string _encodingStatus = "UTF-8";

    public MainWindowViewModel(IWorkspaceFileSystem workspaceFileSystem)
    {
        ArgumentNullException.ThrowIfNull(workspaceFileSystem);
        Explorer = new WorkspaceExplorerViewModel(workspaceFileSystem);
        Problems = new ProblemsViewModel();
        Explorer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceExplorerViewModel.HasWorkspace))
            {
                OnPropertyChanged(nameof(HasWorkspace));
            }
        };
        Problems.Changed += Problems_Changed;
        Documents.CollectionChanged += Documents_CollectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DocumentTabViewModel> Documents { get; } = new();

    public WorkspaceExplorerViewModel Explorer { get; }

    public ProblemsViewModel Problems { get; }

    public ObservableCollection<string> RecentWorkspaces { get; } = new();

    public ObservableCollection<string> OutputLines { get; } = new();

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

    public void AppendOutput(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        OutputLines.Add(line);
        const int maxLines = 2000;
        while (OutputLines.Count > maxLines)
        {
            OutputLines.RemoveAt(0);
        }
    }

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

    private static bool IsRocketPath(string path) =>
        string.Equals(Path.GetExtension(path), ".rocket", StringComparison.OrdinalIgnoreCase);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

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
