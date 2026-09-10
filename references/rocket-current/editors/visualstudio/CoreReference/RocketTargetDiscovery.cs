using System;
using System.IO;

namespace Rocket.VisualStudio.Core
{
    internal static class RocketTargetDiscovery
    {
        internal static RocketTarget Discover(string activePath)
        {
            if (string.IsNullOrWhiteSpace(activePath))
                throw new InvalidOperationException("Open a Rocket source file before using a Rocket command.");

            var fullPath = Path.GetFullPath(activePath);
            var isManifest = string.Equals(Path.GetFileName(fullPath), "rocket.toml", StringComparison.OrdinalIgnoreCase);
            if (!isManifest && !string.Equals(Path.GetExtension(fullPath), ".rocket", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active document is not a .rocket source file or rocket.toml manifest.");

            var startDirectory = isManifest ? Path.GetDirectoryName(fullPath) : Path.GetDirectoryName(fullPath);
            var manifestPath = isManifest ? fullPath : FindNearestManifest(startDirectory);
            if (manifestPath != null)
            {
                var root = Path.GetDirectoryName(manifestPath);
                var manifest = RocketManifest.Read(manifestPath);
                var activeFile = isManifest ? Path.Combine(root, manifest.Entry) : fullPath;
                return new RocketTarget(activeFile, root, root, root, manifest.OutputName, manifest.OutputKind);
            }

            if (isManifest) throw new InvalidOperationException("The active rocket.toml manifest could not be resolved.");
            return new RocketTarget(fullPath, fullPath, startDirectory, null,
                Path.GetFileNameWithoutExtension(fullPath), "executable");
        }

        internal static string FindNearestManifest(string startDirectory)
        {
            var directory = string.IsNullOrWhiteSpace(startDirectory) ? null : new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "rocket.toml");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            return null;
        }

        internal static string FindRepositoryRoot(string startPath)
        {
            var startDirectory = File.Exists(startPath) ? Path.GetDirectoryName(Path.GetFullPath(startPath)) : Path.GetFullPath(startPath);
            var directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "dependencies", "activate.ps1")) &&
                    File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt"))) return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        internal static string ResolveWorkingDirectory(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath)) return Directory.GetCurrentDirectory();
            var fullPath = Path.GetFullPath(startPath);
            if (Directory.Exists(fullPath)) return fullPath;
            var directory = Path.GetDirectoryName(fullPath);
            return string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
        }
    }
}
