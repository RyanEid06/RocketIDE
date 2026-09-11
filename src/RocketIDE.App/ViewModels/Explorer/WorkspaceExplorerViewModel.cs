using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.App.ViewModels.Explorer;

public sealed class WorkspaceExplorerViewModel : INotifyPropertyChanged
{
    private readonly IWorkspaceFileSystem _fileSystem;
    private WorkspaceRoot? _workspace;

    public WorkspaceExplorerViewModel(IWorkspaceFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ExplorerNodeViewModel> Roots { get; } = new();

    public WorkspaceRoot? Workspace
    {
        get => _workspace;
        private set
        {
            if (Equals(_workspace, value))
            {
                return;
            }

            _workspace = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWorkspace));
            OnPropertyChanged(nameof(WorkspaceDisplayName));
        }
    }

    public bool HasWorkspace => Workspace is not null;

    public string WorkspaceDisplayName => Workspace?.Name ?? "No workspace open";

    public async Task OpenAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Workspace '{fullPath}' does not exist.");
        }

        var workspace = new WorkspaceRoot(fullPath);
        var rootEntry = new WorkspaceEntry(fullPath, workspace.Name, IsDirectory: true, HasChildren: true);
        var root = new ExplorerNodeViewModel(_fileSystem, rootEntry) { IsExpanded = true };
        await root.LoadChildrenAsync(cancellationToken);

        Workspace = workspace;
        Roots.Clear();
        Roots.Add(root);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Roots.Count == 0)
        {
            return;
        }

        await Roots[0].RefreshAsync(cancellationToken);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
