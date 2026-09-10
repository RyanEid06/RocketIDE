using System;
using System.IO;

namespace Rocket.VisualStudio.Core
{
    internal sealed class RocketManifest
    {
        internal string PackageName { get; private set; }
        internal string Entry { get; private set; } = Path.Combine("src", "main.rocket");
        internal string OutputKind { get; private set; } = "executable";
        internal string OutputName { get; private set; } = "main";

        internal static RocketManifest Read(string path)
        {
            var manifest = new RocketManifest();
            var section = string.Empty;
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals <= 0) continue;
                var key = line.Substring(0, equals).Trim();
                var value = Unquote(line.Substring(equals + 1).Trim());
                if (string.Equals(section, "package", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase)) manifest.PackageName = value;
                    else if (string.Equals(key, "entry", StringComparison.OrdinalIgnoreCase)) manifest.Entry = value;
                }
                else if (string.Equals(section, "build", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(key, "kind", StringComparison.OrdinalIgnoreCase)) manifest.OutputKind = value;
                    else if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase)) manifest.OutputName = value;
                }
            }

            if (!string.Equals(manifest.OutputKind, "executable", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(manifest.OutputName, "main", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(manifest.PackageName))
            {
                manifest.OutputName = manifest.PackageName;
            }
            return manifest;
        }

        private static string StripComment(string line)
        {
            var quoted = false;
            for (var index = 0; index < line.Length; ++index)
            {
                if (line[index] == '"' && (index == 0 || line[index - 1] != '\\')) quoted = !quoted;
                if (line[index] == '#' && !quoted) return line.Substring(0, index);
            }
            return line;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return value;
        }
    }
}
