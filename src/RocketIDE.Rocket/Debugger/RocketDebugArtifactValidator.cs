using System.Text.Json;

namespace RocketIDE.Rocket.Debugger;

public sealed record RocketDebugArtifactValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Sources)
{
    public string Summary => IsValid
        ? $"Validated executable, PDB, and {Sources.Count} source-map source(s)."
        : string.Join(" ", Errors);
}

public static class RocketDebugArtifactValidator
{
    public static RocketDebugArtifactValidationResult Validate(
        string executablePath,
        string pdbPath,
        string sourceMapPath,
        string sourceRoot)
    {
        var errors = new List<string>();
        var sources = new List<string>();
        if (!File.Exists(executablePath)) errors.Add($"Executable not found: {executablePath}");
        if (!File.Exists(pdbPath)) errors.Add($"PDB not found: {pdbPath}");
        if (!File.Exists(sourceMapPath)) errors.Add($"Source map not found: {sourceMapPath}");
        if (!Directory.Exists(sourceRoot)) errors.Add($"Source root not found: {sourceRoot}");
        if (errors.Count > 0) return new RocketDebugArtifactValidationResult(false, errors, sources);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(sourceMapPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("format", out var format) ||
                !string.Equals(format.GetString(), "rocket-source-map-1", StringComparison.Ordinal))
            {
                errors.Add("Source map format is not rocket-source-map-1.");
                return new RocketDebugArtifactValidationResult(false, errors, sources);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("functions", out var functions) && functions.ValueKind == JsonValueKind.Array)
            {
                foreach (var function in functions.EnumerateArray())
                {
                    AddSource(function, sourceRoot, seen, sources, errors);
                    if (function.TryGetProperty("locations", out var locations) && locations.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var location in locations.EnumerateArray())
                        {
                            AddSource(location, sourceRoot, seen, sources, errors);
                        }
                    }
                }
            }
            else
            {
                errors.Add("Source map does not contain a functions array.");
            }
        }
        catch (JsonException exception)
        {
            errors.Add($"Source map is invalid JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            errors.Add($"Source map could not be read: {exception.Message}");
        }

        return new RocketDebugArtifactValidationResult(errors.Count == 0, errors, sources);
    }

    private static void AddSource(
        JsonElement value,
        string sourceRoot,
        HashSet<string> seen,
        List<string> sources,
        List<string> errors)
    {
        if (!value.TryGetProperty("source", out var sourceProperty) || sourceProperty.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var source = sourceProperty.GetString();
        if (string.IsNullOrWhiteSpace(source) || !seen.Add(source)) return;
        sources.Add(source);
        var candidate = Path.IsPathRooted(source) ? source : Path.Combine(sourceRoot, source);
        if (File.Exists(candidate)) return;

        var basename = Path.GetFileName(source);
        var matches = Directory.EnumerateFiles(sourceRoot, basename, SearchOption.AllDirectories).Take(2).ToArray();
        if (matches.Length == 0)
        {
            errors.Add($"Mapped source was not found: {source}");
        }
        else if (matches.Length > 1)
        {
            errors.Add($"Mapped source basename is ambiguous: {source}");
        }
    }
}
