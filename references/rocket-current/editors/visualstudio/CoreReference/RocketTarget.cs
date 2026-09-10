using System;
using System.IO;

namespace Rocket.VisualStudio.Core
{
    internal sealed class RocketTarget
    {
        internal RocketTarget(string activeFile, string compilerInput, string workingDirectory,
            string packageRoot, string artifactName, string outputKind)
        {
            ActiveFile = Path.GetFullPath(activeFile);
            CompilerInput = Path.GetFullPath(compilerInput);
            WorkingDirectory = Path.GetFullPath(workingDirectory);
            PackageRoot = packageRoot == null ? null : Path.GetFullPath(packageRoot);
            ArtifactName = artifactName;
            OutputKind = outputKind;
        }

        internal string ActiveFile { get; }
        internal string CompilerInput { get; }
        internal string WorkingDirectory { get; }
        internal string PackageRoot { get; }
        internal string ArtifactName { get; }
        internal string OutputKind { get; }
        internal bool IsPackage => PackageRoot != null;
        internal bool IsExecutable => string.Equals(OutputKind, "executable", StringComparison.OrdinalIgnoreCase);

        internal string ArtifactPath => Path.Combine(
            PackageRoot ?? Path.GetDirectoryName(ActiveFile), ".rocketc", ArtifactName + ArtifactExtension);

        internal string SourceMapPath => Path.Combine(
            PackageRoot ?? Path.GetDirectoryName(ActiveFile), ".rocketc", ArtifactName + ".rocket.map.json");

        private string ArtifactExtension
        {
            get
            {
                if (string.Equals(OutputKind, "static-library", StringComparison.OrdinalIgnoreCase)) return ".lib";
                if (string.Equals(OutputKind, "dynamic-library", StringComparison.OrdinalIgnoreCase)) return ".dll";
                return ".exe";
            }
        }
    }
}
