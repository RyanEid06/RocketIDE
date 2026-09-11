using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.App.ViewModels.Explorer;

public sealed class ExplorerNodeViewModel : INotifyPropertyChanged
{
    private readonly IWorkspaceFileSystem _fileSystem;
    private bool _isExpanded;
    private bool _isLoaded;
    private bool _isLoading;

    public ExplorerNodeViewModel(IWorkspaceFileSystem fileSystem, WorkspaceEntry entry)
    {
        _fileSystem = fileSystem;
        Entry = entry;
        if (entry.IsDirectory && entry.HasChildren)
        {
            Children.Add(CreatePlaceholder(fileSystem));
        }
    }

    private ExplorerNodeViewModel(IWorkspaceFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        Entry = new WorkspaceEntry(string.Empty, string.Empty, IsDirectory: false, HasChildren: false);
        IsPlaceholder = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceEntry Entry { get; private set; }

    public string Path => Entry.Path;

    public string Name => Entry.Name;

    public bool IsDirectory => Entry.IsDirectory;

    public bool IsPlaceholder { get; }

    public ObservableCollection<ExplorerNodeViewModel> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public async Task LoadChildrenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsDirectory || IsPlaceholder || _isLoaded || IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var entries = await _fileSystem.GetChildrenAsync(Path, cancellationToken);
            Children.Clear();
            foreach (var entry in entries)
            {
                Children.Add(new ExplorerNodeViewModel(_fileSystem, entry));
            }

            _isLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!IsDirectory || IsPlaceholder)
        {
            return;
        }

        var expandedPaths = CaptureExpandedDirectoryPaths();
        _isLoaded = false;
        await LoadChildrenAsync(cancellationToken);
        await RestoreExpandedPathsAsync(expandedPaths, cancellationToken);
    }

    private HashSet<string> CaptureExpandedDirectoryPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CaptureExpandedDirectoryPaths(paths);
        return paths;
    }

    private void CaptureExpandedDirectoryPaths(HashSet<string> paths)
    {
        if (IsDirectory && IsExpanded && !string.IsNullOrEmpty(Path))
        {
            paths.Add(Path);
        }

        foreach (var child in Children.Where(child => !child.IsPlaceholder))
        {
            child.CaptureExpandedDirectoryPaths(paths);
        }
    }

    private async Task RestoreExpandedPathsAsync(HashSet<string> expandedPaths, CancellationToken cancellationToken)
    {
        foreach (var child in Children.Where(child => child.IsDirectory && expandedPaths.Contains(child.Path)))
        {
            child.IsExpanded = true;
            await child.LoadChildrenAsync(cancellationToken);
            await child.RestoreExpandedPathsAsync(expandedPaths, cancellationToken);
        }
    }

    private static ExplorerNodeViewModel CreatePlaceholder(IWorkspaceFileSystem fileSystem) => new(fileSystem);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
