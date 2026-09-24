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
        var maximumDirectories = maxFiles >= int.MaxValue / 2 ? int.MaxValue : Math.Max(256, maxFiles * 2);
        var pending = new Stack<string>();
        var visited = new HashSet<string>(PathComparer);
        var files = new List<string>(Math.Min(maxFiles, 512));
        pending.Push(root);

        while (pending.Count > 0 && files.Count < maxFiles && visited.Count < maximumDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetFullPath(pending.Pop());
            if (!visited.Add(directory))
            {
                continue;
            }

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
                    if (visited.Count + pending.Count < maximumDirectories)
                    {
                        pending.Push(child.Path);
                    }
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

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
