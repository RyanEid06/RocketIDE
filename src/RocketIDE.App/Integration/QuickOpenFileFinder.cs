using System.IO;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.App.Integration;

public sealed class QuickOpenFileFinder(IWorkspaceFileSystem fileSystem)
{
    private readonly IWorkspaceFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<IReadOnlyList<string>> FindAsync(
        string rootPath,
        int maxFiles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (maxFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles));
        }

        var root = Path.GetFullPath(rootPath);
        var pending = new Stack<string>();
        var files = new List<string>(Math.Min(maxFiles, 512));
        pending.Push(root);

        while (pending.Count > 0 && files.Count < maxFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IReadOnlyList<WorkspaceEntry> children;
            try
            {
                children = await _fileSystem.GetChildrenAsync(directory, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child.IsDirectory)
                {
                    pending.Push(child.Path);
                }
                else
                {
                    files.Add(child.Path);
                    if (files.Count >= maxFiles)
                    {
                        break;
                    }
                }
            }
        }

        return files;
    }
}
