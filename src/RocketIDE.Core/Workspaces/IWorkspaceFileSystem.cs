namespace RocketIDE.Core.Workspaces;

public interface IWorkspaceFileSystem
{
    Task<IReadOnlyList<WorkspaceEntry>> GetChildrenAsync(string directoryPath, CancellationToken cancellationToken);
    Task CreateFileAsync(string path, CancellationToken cancellationToken);
    void CreateDirectory(string path);
    string Rename(string path, string newName);
    void DeleteToRecycleBin(string path);
    string ResolveChildPath(string directoryPath, string leafName);
}
