using System.Text.Json;

namespace RocketIDE.Debugger;

public sealed class RocketDebugSourceMap
{
    private readonly IReadOnlyDictionary<string, string> _byBasename;

    private RocketDebugSourceMap(string mapPath, IReadOnlyList<string> sources, IReadOnlyDictionary<string, string> byBasename)
    {
        MapPath = mapPath;
        Sources = sources;
        _byBasename = byBasename;
    }

    public string MapPath { get; }
    public IReadOnlyList<string> Sources { get; }

    public static RocketDebugSourceMap Read(string mapPath, string sourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        mapPath = Path.GetFullPath(mapPath);
        sourceRoot = Path.GetFullPath(sourceRoot);
        if (!File.Exists(mapPath)) throw new FileNotFoundException("Rocket debug source map was not found.", mapPath);
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException($"Rocket debug source root was not found: {sourceRoot}");

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(mapPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("format", out var format) ||
                !string.Equals(format.GetString(), "rocket-source-map-1", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The Rocket source map has an unsupported format.");
            }
            if (!root.TryGetProperty("functions", out var functions) || functions.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The Rocket source map does not contain a functions array.");
            }

            var sourceValues = new List<string>();
            var seenSourceValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var function in functions.EnumerateArray())
            {
                AddSourceValue(function, sourceValues, seenSourceValues);
                if (function.TryGetProperty("locations", out var locations) && locations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var location in locations.EnumerateArray()) AddSourceValue(location, sourceValues, seenSourceValues);
                }
            }

            var resolved = new List<string>(sourceValues.Count);
            var byBasename = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sourceValues)
            {
                var path = ResolveRecordedSource(source, sourceRoot);
                var basename = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(basename))
                {
                    throw new InvalidDataException($"Mapped Rocket source does not have a file name: {source}");
                }
                if (byBasename.TryGetValue(basename, out var existing) &&
                    !string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Rocket debug source basename is ambiguous: {basename}");
                }
                byBasename[basename] = path;
                if (!resolved.Contains(path, StringComparer.OrdinalIgnoreCase)) resolved.Add(path);
            }

            if (resolved.Count == 0)
            {
                throw new InvalidDataException("The Rocket source map does not contain any source files.");
            }

            return new RocketDebugSourceMap(mapPath, resolved, byBasename);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Rocket source map contains invalid JSON.", exception);
        }
    }

    public string? TryResolveDebuggerSource(string debuggerSource)
    {
        if (string.IsNullOrWhiteSpace(debuggerSource)) return null;
        var normalized = debuggerSource.Replace('/', '\\');
        var basename = Path.GetFileName(normalized);
        return !string.IsNullOrWhiteSpace(basename) && _byBasename.TryGetValue(basename, out var path) ? path : null;
    }

    public string ResolveDebuggerSource(string debuggerSource) =>
        TryResolveDebuggerSource(debuggerSource)
        ?? throw new KeyNotFoundException($"Debugger source '{debuggerSource}' is not present in the Rocket source map.");

    private static void AddSourceValue(JsonElement element, List<string> destination, HashSet<string> seen)
    {
        if (!element.TryGetProperty("source", out var value) || value.ValueKind != JsonValueKind.String) return;
        var source = value.GetString();
        if (!string.IsNullOrWhiteSpace(source) && seen.Add(source)) destination.Add(source);
    }

    private static string ResolveRecordedSource(string source, string sourceRoot)
    {
        var direct = Path.IsPathFullyQualified(source) ? Path.GetFullPath(source) : Path.GetFullPath(Path.Combine(sourceRoot, source));
        if (File.Exists(direct)) return direct;

        var basename = Path.GetFileName(source.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(basename)) throw new FileNotFoundException($"Mapped Rocket source was not found: {source}");
        var matches = Directory.EnumerateFiles(sourceRoot, basename, SearchOption.AllDirectories).Take(2).Select(Path.GetFullPath).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"Mapped Rocket source was not found: {source}"),
            _ => throw new InvalidDataException($"Rocket debug source basename is ambiguous: {basename}"),
        };
    }
}
