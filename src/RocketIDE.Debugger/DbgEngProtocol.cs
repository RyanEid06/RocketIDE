using System.Globalization;
using System.Text.RegularExpressions;

namespace RocketIDE.Debugger;

public static partial class DbgEngProtocol
{
    public static string BuildSourceBreakpointCommand(string basename, int line)
    {
        ValidateSourceBasename(basename);
        if (line < 1) throw new ArgumentOutOfRangeException(nameof(line));
        return $"bp `{basename}:{line.ToString(CultureInfo.InvariantCulture)}`";
    }

    public static string BuildSourcePathCommand(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var path = Path.GetFullPath(directory);
        if (path.Contains('"') || path.Contains('\r') || path.Contains('\n'))
        {
            throw new ArgumentException("Debugger source paths cannot contain quotes or newlines.", nameof(directory));
        }
        return $".srcpath+ \"{path}\"";
    }

    public static string BuildSelectThreadCommand(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return $"~{index.ToString(CultureInfo.InvariantCulture)}s";
    }

    public static string BuildSelectFrameCommand(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return $".frame {index.ToString(CultureInfo.InvariantCulture)}";
    }

    public static IReadOnlyList<RocketDebugThread> ParseThreads(string output)
    {
        var result = new List<RocketDebugThread>();
        foreach (var raw in SplitLines(output))
        {
            var match = ThreadRegex().Match(raw);
            if (!match.Success) continue;
            var current = match.Groups["current"].Value == ".";
            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) continue;
            var tidText = match.Groups["tid"].Value;
            if (!int.TryParse(tidText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var tid)) continue;
            result.Add(new RocketDebugThread(index, tid, $"Thread {index} (0x{tid:x})", current));
        }
        return result;
    }

    public static IReadOnlyList<RocketDebugStackFrame> ParseStackFrames(string output, RocketDebugSourceMap sourceMap)
    {
        ArgumentNullException.ThrowIfNull(sourceMap);
        var result = new List<RocketDebugStackFrame>();
        foreach (var raw in SplitLines(output))
        {
            var match = FrameRegex().Match(raw);
            if (!match.Success) continue;
            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var index)) continue;
            var body = match.Groups["body"].Value.Trim();
            var sourceMatch = SourceLocationRegex().Match(body);
            string? sourcePath = null;
            int? line = null;
            if (sourceMatch.Success && int.TryParse(sourceMatch.Groups["line"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLine))
            {
                sourcePath = sourceMap.TryResolveDebuggerSource(sourceMatch.Groups["source"].Value);
                line = sourcePath is null ? null : parsedLine;
            }

            var function = body;
            var bracket = body.IndexOf(" [", StringComparison.Ordinal);
            if (bracket >= 0) function = body[..bracket].Trim();
            // k/kn output begins with stack/return addresses. Preserve the debugger's call-site text,
            // but remove the first two obvious hex address columns when present.
            function = LeadingAddressesRegex().Replace(function, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(function)) function = body;
            result.Add(new RocketDebugStackFrame(index, function, null, sourcePath, line));
        }
        return result;
    }

    public static IReadOnlyList<RocketDebugVariable> ParseLocals(string output)
    {
        var result = new List<RocketDebugVariable>();
        foreach (var raw in SplitLines(output))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("***", StringComparison.Ordinal)) continue;
            var match = LocalRegex().Match(line);
            if (match.Success)
            {
                result.Add(new RocketDebugVariable(
                    match.Groups["name"].Value.TrimStart('*', '&'),
                    match.Groups["type"].Value.Trim(),
                    match.Groups["value"].Value.Trim()));
            }
            else
            {
                // Unknown DbgEng text stays visible verbatim rather than being interpreted as Rocket data.
                result.Add(new RocketDebugVariable(line, null, line));
            }
        }
        return result;
    }

    public static RocketDebugStopLocation? ParseCurrentLocation(string output, RocketDebugSourceMap sourceMap, string reason)
    {
        ArgumentNullException.ThrowIfNull(sourceMap);
        foreach (var line in SplitLines(output))
        {
            var match = SourceLocationRegex().Match(line);
            if (!match.Success) continue;
            var source = sourceMap.TryResolveDebuggerSource(match.Groups["source"].Value);
            if (source is null || !int.TryParse(match.Groups["line"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourceLine)) continue;
            return new RocketDebugStopLocation(source, sourceLine, reason);
        }
        return null;
    }

    public static int? ParseCurrentProcessId(string output)
    {
        foreach (var line in SplitLines(output))
        {
            var match = ProcessRegex().Match(line);
            if (!match.Success) continue;
            var value = match.Groups["pid"].Value;
            if (int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pid)) return pid;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pid)) return pid;
        }
        return null;
    }

    private static void ValidateSourceBasename(string basename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basename);
        if (!string.Equals(Path.GetFileName(basename), basename, StringComparison.Ordinal) ||
            basename.IndexOfAny(['`', ';', '\r', '\n', '"']) >= 0)
        {
            throw new ArgumentException("Debugger source identity must be a safe basename.", nameof(basename));
        }
    }

    private static IEnumerable<string> SplitLines(string? value) =>
        (value ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [GeneratedRegex(@"^\s*(?<current>\.)?\s*(?<index>\d+)\s+Id:\s*[0-9a-fA-F]+\.(?<tid>[0-9a-fA-F]+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ThreadRegex();

    [GeneratedRegex(@"^\s*(?<index>[0-9a-fA-F]{1,3})\s+(?<body>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex FrameRegex();

    [GeneratedRegex(@"\[?(?<source>[^\[\]\r\n]*?\.rocket)\s*@\s*(?<line>\d+)\]?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceLocationRegex();

    [GeneratedRegex(@"^(?:[0-9a-fA-F`]{4,}\s+){1,2}", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingAddressesRegex();

    [GeneratedRegex(@"^(?<type>.+?)\s+(?<name>[*&]*[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalRegex();

    [GeneratedRegex(@"^\s*\.\s*\d+\s+id:\s*(?<pid>[0-9a-fA-F]+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProcessRegex();
}
