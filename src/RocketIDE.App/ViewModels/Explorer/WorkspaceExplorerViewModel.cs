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

    public void CollapseAll()
    {
        foreach (var root in Roots)
        {
            root.CollapseRecursively();
        }
    }

    public async Task<ExplorerNodeViewModel?> RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Workspace is null || Roots.Count == 0)
        {
            return null;
        }

        var targetPath = Path.GetFullPath(path);
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return null;
        }

        var rootPath = Path.GetFullPath(Workspace.Path);
        var relative = Path.GetRelativePath(rootPath, targetPath);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        var root = Roots[0];
        root.ClearSelectionRecursively();
        root.IsExpanded = true;
        if (PathComparer.Equals(root.Path, targetPath))
        {
            root.IsSelected = true;
            return root;
        }

        var current = root;
        var parts = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!current.IsDirectory)
            {
                return null;
            }

            current.IsExpanded = true;
            await current.LoadChildrenAsync(cancellationToken);
            var expectedPath = Path.GetFullPath(Path.Combine(current.Path, part));
            var next = current.Children.FirstOrDefault(child =>
                !child.IsPlaceholder && PathComparer.Equals(child.Path, expectedPath));
            if (next is null)
            {
                return null;
            }
            current = next;
        }

        current.IsSelected = true;
        return current;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
