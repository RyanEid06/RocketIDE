namespace RocketIDE.Rocket.Projects;

public sealed class RocketTargetDiscovery : IRocketTargetDiscovery
{
    public RocketTarget? Discover(string activePath)
    {
        if (string.IsNullOrWhiteSpace(activePath))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(activePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        var isManifest = string.Equals(Path.GetFileName(fullPath), "rocket.toml", StringComparison.OrdinalIgnoreCase);
        var isRocketFile = string.Equals(Path.GetExtension(fullPath), ".rocket", StringComparison.OrdinalIgnoreCase);
        if (!isManifest && !isRocketFile)
        {
            return null;
        }

        var startDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        var manifestPath = isManifest ? fullPath : FindNearestManifest(startDirectory);
        if (manifestPath is not null)
        {
            var root = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var input = isManifest
                ? Path.GetFullPath(Path.Combine(root, RocketManifest.Read(manifestPath).Entry))
                : fullPath;

            return new RocketTarget(input, Path.GetFullPath(root), Path.GetFullPath(manifestPath), IsStandalone: false);
        }

        return isManifest
            ? null
            : new RocketTarget(fullPath, Path.GetFullPath(startDirectory), ManifestPath: null, IsStandalone: true);
    }

    public static string? FindNearestManifest(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "rocket.toml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
