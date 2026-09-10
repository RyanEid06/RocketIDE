using Microsoft.VisualBasic.FileIO;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.Infrastructure.Files;

public sealed class WorkspaceFileSystem
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".rocketc",
        ".vs",
        "bin",
        "obj",
    };

    public Task<IReadOnlyList<WorkspaceEntry>> GetChildrenAsync(string directoryPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var fullPath = Path.GetFullPath(directoryPath);

        return Task.Run<IReadOnlyList<WorkspaceEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<WorkspaceEntry>();

            foreach (var directory in Directory.EnumerateDirectories(fullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(directory);
                if (ShouldHideDirectory(name))
                {
                    continue;
                }

                entries.Add(new WorkspaceEntry(
                    Path.GetFullPath(directory),
                    name,
                    IsDirectory: true,
                    HasChildren: HasVisibleChildren(directory)));
            }

            foreach (var file in Directory.EnumerateFiles(fullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(new WorkspaceEntry(Path.GetFullPath(file), Path.GetFileName(file), IsDirectory: false, HasChildren: false));
            }

            return entries
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public async Task CreateFileAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new IOException($"'{fullPath}' already exists.");
        }

        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException($"The parent directory for '{fullPath}' does not exist.");
        }

        await using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1, FileOptions.Asynchronous);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void CreateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new IOException($"'{fullPath}' already exists.");
        }

        Directory.CreateDirectory(fullPath);
    }

    public string Rename(string path, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName is "." or "..")
        {
            throw new ArgumentException("The new name contains invalid file-name characters.", nameof(newName));
        }

        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath) ?? throw new IOException($"Cannot determine the parent directory for '{fullPath}'.");
        var destination = Path.Combine(parent, newName.Trim());
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException($"'{destination}' already exists.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Move(fullPath, destination);
        }
        else if (File.Exists(fullPath))
        {
            File.Move(fullPath, destination);
        }
        else
        {
            throw new FileNotFoundException("The item to rename no longer exists.", fullPath);
        }

        return Path.GetFullPath(destination);
    }

    public void DeleteToRecycleBin(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            FileSystem.DeleteDirectory(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return;
        }

        if (File.Exists(fullPath))
        {
            FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return;
        }

        throw new FileNotFoundException("The item to delete no longer exists.", fullPath);
    }

    public static bool ShouldHideDirectory(string name) => ExcludedDirectoryNames.Contains(name);

    private static bool HasVisibleChildren(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory)
                    .Select(Path.GetFileName)
                    .Any(name => !string.IsNullOrEmpty(name) && !ShouldHideDirectory(name)) ||
                Directory.EnumerateFiles(directory).Any();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
