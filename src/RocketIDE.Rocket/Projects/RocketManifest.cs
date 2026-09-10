namespace RocketIDE.Rocket.Projects;

public sealed class RocketManifest
{
    public string? PackageName { get; private set; }

    public string Entry { get; private set; } = Path.Combine("src", "main.rocket");

    public string OutputKind { get; private set; } = "executable";

    public string OutputName { get; private set; } = "main";

    public static RocketManifest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var manifest = new RocketManifest();
        var section = string.Empty;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = Unquote(line[(equals + 1)..].Trim());
            if (string.Equals(section, "package", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                {
                    manifest.PackageName = value;
                }
                else if (string.Equals(key, "entry", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                {
                    manifest.Entry = value.Replace('/', Path.DirectorySeparatorChar);
                }
            }
            else if (string.Equals(section, "build", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(key, "kind", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                {
                    manifest.OutputKind = value;
                }
                else if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                {
                    manifest.OutputName = value;
                }
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
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && quote != '\0')
            {
                escaped = true;
                continue;
            }

            if (character is '"' or '\'')
            {
                if (quote == '\0')
                {
                    quote = character;
                }
                else if (quote == character)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character == '#' && quote == '\0')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            var quote = value[0];
            var unquoted = value[1..^1];
            return unquoted
                .Replace("\\\\", "\\", StringComparison.Ordinal)
                .Replace($"\\{quote}", quote.ToString(), StringComparison.Ordinal);
        }

        return value;
    }
}
