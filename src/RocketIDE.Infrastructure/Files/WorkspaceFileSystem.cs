using Microsoft.VisualBasic.FileIO;
using RocketIDE.Core.Workspaces;

namespace RocketIDE.Infrastructure.Files;

public sealed class WorkspaceFileSystem : IWorkspaceFileSystem
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".rocketc",
        ".vs",
        "bin",
        "obj",
        "out",
    };

    private static readonly HashSet<string> ExcludedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".obj",
        ".o",
    };

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
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
                var name = Path.GetFileName(file);
                if (ShouldHideFile(name))
                {
                    continue;
                }

                entries.Add(new WorkspaceEntry(Path.GetFullPath(file), name, IsDirectory: false, HasChildren: false));
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

    public string ResolveChildPath(string directoryPath, string leafName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var directory = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory '{directory}' does not exist.");
        }

        var safeName = ValidateLeafName(leafName, nameof(leafName));
        var candidate = Path.GetFullPath(Path.Combine(directory, safeName));
        var relative = Path.GetRelativePath(directory, candidate);
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("The name must stay inside the selected directory.", nameof(leafName));
        }

        return candidate;
    }

    public string Rename(string path, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath) ?? throw new IOException($"Cannot determine the parent directory for '{fullPath}'.");
        var destination = ResolveChildPath(parent, newName);
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

    private static string ValidateLeafName(string name, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameterName);
        var trimmed = name.Trim();
        var dotIndex = trimmed.IndexOf('.');
        var deviceName = (dotIndex < 0 ? trimmed : trimmed[..dotIndex]).TrimEnd(' ', '.');
        if (!string.Equals(trimmed, name, StringComparison.Ordinal) ||
            trimmed is "." or ".." ||
            trimmed.EndsWith('.') ||
            ReservedWindowsNames.Contains(deviceName) ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar) ||
            !string.Equals(Path.GetFileName(trimmed), trimmed, StringComparison.Ordinal))
        {
            throw new ArgumentException("The name must be a single valid Windows file or folder name with no path components.", parameterName);
        }

        return trimmed;
    }

    internal static bool ShouldHideDirectory(string name) => ExcludedDirectoryNames.Contains(name);

    internal static bool ShouldHideFile(string name) =>
        ExcludedFileExtensions.Contains(Path.GetExtension(name));

    private static bool HasVisibleChildren(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory)
                    .Select(Path.GetFileName)
                    .Any(name => !string.IsNullOrEmpty(name) && !ShouldHideDirectory(name)) ||
                Directory.EnumerateFiles(directory)
                    .Select(Path.GetFileName)
                    .Any(name => !string.IsNullOrEmpty(name) && !ShouldHideFile(name));
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
