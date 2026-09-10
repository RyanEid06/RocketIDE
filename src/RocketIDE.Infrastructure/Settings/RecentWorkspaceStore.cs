using System.Text.Json;

namespace RocketIDE.Infrastructure.Settings;

public sealed class RecentWorkspaceStore
{
    public const int DefaultMaxEntries = 8;

    private readonly string _settingsPath;
    private readonly int _maxEntries;

    public RecentWorkspaceStore(string settingsPath, int maxEntries = DefaultMaxEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        _settingsPath = Path.GetFullPath(settingsPath);
        _maxEntries = maxEntries;
    }

    public static RecentWorkspaceStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsPath = Path.Combine(localAppData, "RocketIDE", "recent-workspaces.json");
        return new RecentWorkspaceStore(settingsPath);
    }

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            await using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            var paths = await JsonSerializer.DeserializeAsync<string[]>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Normalize(paths ?? Array.Empty<string>());
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public async Task SaveAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var normalized = Normalize(paths);
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new IOException($"Cannot determine the settings directory for '{_settingsPath}'.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private IReadOnlyList<string> Normalize(IEnumerable<string> paths)
    {
        var normalized = new List<string>(_maxEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (normalized.Count >= _maxEntries)
            {
                break;
            }

            if (!TryNormalizeDirectoryPath(path, out var fullPath) || !seen.Add(fullPath))
            {
                continue;
            }

            normalized.Add(fullPath);
        }

        return normalized;
    }

    private static bool TryNormalizeDirectoryPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var candidate = Path.GetFullPath(path);
            var root = Path.GetPathRoot(candidate);
            if (!string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
