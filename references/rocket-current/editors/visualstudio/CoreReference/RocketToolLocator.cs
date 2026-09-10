using System;
using System.Collections.Generic;
using System.IO;

namespace Rocket.VisualStudio.Core
{
    internal static class RocketToolLocator
    {
        internal static string FindCompiler(string configuredPath, string activePath)
        {
            return FindTool("rocketc.exe", configuredPath, "ROCKET_COMPILER", activePath, null);
        }

        internal static string FindLanguageServer(string configuredPath, string activePath, string compilerPath)
        {
            return FindTool("rocket-lsp.exe", configuredPath, "ROCKET_LANGUAGE_SERVER", activePath, compilerPath);
        }

        private static string FindTool(string fileName, string configuredPath, string environmentName,
            string activePath, string siblingPath)
        {
            foreach (var candidate in Candidates(fileName, configuredPath, environmentName, activePath, siblingPath))
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            return null;
        }

        private static IEnumerable<string> Candidates(string fileName, string configuredPath, string environmentName,
            string activePath, string siblingPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
                yield return Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));

            var environmentPath = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(environmentPath)) yield return Environment.ExpandEnvironmentVariables(environmentPath);

            if (!string.IsNullOrWhiteSpace(siblingPath))
                yield return Path.Combine(Path.GetDirectoryName(siblingPath), fileName);

            var repository = string.IsNullOrWhiteSpace(activePath) ? null : RocketTargetDiscovery.FindRepositoryRoot(activePath);
            if (repository != null)
            {
                yield return Path.Combine(repository, "out", "build", "windows-debug", fileName);
                yield return Path.Combine(repository, "out", "build", "windows-release", fileName);
                yield return Path.Combine(repository, "out", "package", "bin", fileName);
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                yield return Path.Combine(directory.Trim().Trim('"'), fileName);
        }
    }
}
