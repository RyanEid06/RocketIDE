using System.Text.Json;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Compiler;

public static class RocketMessageParser
{
    public static bool TryParse(string line, out RocketMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(line) || line.AsSpan().TrimStart()[0] != '{')
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            if (!TryGetString(root, "schema", out var schema) || !string.Equals(schema, "rocket-message-1", StringComparison.Ordinal) ||
                !TryGetString(root, "reason", out var reason) || string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            RocketMessageSpan? span = null;
            if (root.TryGetProperty("span", out var spanElement) && spanElement.ValueKind == JsonValueKind.Object)
            {
                span = new RocketMessageSpan(
                    GetString(spanElement, "file"),
                    GetInt(spanElement, "line"),
                    GetInt(spanElement, "column"));
            }

            message = new RocketMessage(
                reason,
                GetString(root, "level"),
                GetString(root, "code"),
                GetString(root, "message"),
                span,
                GetString(root, "command"),
                GetBool(root, "success"),
                GetString(root, "artifact"),
                GetString(root, "cache"),
                GetString(root, "name"),
                GetString(root, "status"),
                GetInt(root, "exitCode"),
                GetInt(root, "passed"),
                GetInt(root, "failed"),
                GetInt(root, "expectedFailures"),
                GetInt(root, "selected"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string FormatForOutput(RocketMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Reason switch
        {
            "diagnostic" => FormatDiagnostic(message),
            "build-finished" => FormatBuildFinished(message),
            "test-started" => string.IsNullOrWhiteSpace(message.Name) ? "test started" : $"test {message.Name}",
            "test-finished" => FormatTestFinished(message),
            "test-summary" => $"{message.Passed ?? 0} passed; {message.Failed ?? 0} failed; {message.ExpectedFailures ?? 0} expected failure(s); {message.Selected ?? 0} selected",
            _ => message.Message ?? message.Reason,
        };
    }

    public static bool TryMapDiagnostic(
        RocketMessage message,
        RocketTarget target,
        out RocketDiagnostic? diagnostic,
        string provenance = "Compiler")
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(target);
        diagnostic = null;
        if (!string.Equals(message.Reason, "diagnostic", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(message.Message) ||
            message.Span is not { File: { Length: > 0 } file, Line: > 0, Column: > 0 } span)
        {
            return false;
        }

        string path;
        try
        {
            path = Path.IsPathRooted(file)
                ? Path.GetFullPath(file)
                : Path.GetFullPath(Path.Combine(target.WorkingDirectory, file));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var line = span.Line.Value - 1;
        var column = span.Column.Value - 1;
        diagnostic = new RocketDiagnostic(
            "rocketc",
            message.Code ?? string.Empty,
            message.Message,
            MapSeverity(message.Level),
            path,
            new SourceRange(line, column, line, column + 1),
            Provenance: string.IsNullOrWhiteSpace(provenance) ? "Compiler" : provenance);
        return true;
    }

    private static string FormatDiagnostic(RocketMessage message)
    {
        var location = message.Span is { File: { Length: > 0 } file }
            ? $"{file}({message.Span.Line ?? 0},{message.Span.Column ?? 0}): "
            : string.Empty;
        var level = string.IsNullOrWhiteSpace(message.Level) ? "diagnostic" : message.Level;
        var code = string.IsNullOrWhiteSpace(message.Code) ? string.Empty : $" {message.Code}";
        var text = message.Message ?? string.Empty;
        return $"{location}{level}{code}: {text}";
    }

    private static string FormatBuildFinished(RocketMessage message)
    {
        var command = string.IsNullOrWhiteSpace(message.Command) ? "build" : message.Command;
        var success = message.Success switch
        {
            true => "succeeded",
            false => "failed",
            null => "finished",
        };
        var result = string.IsNullOrWhiteSpace(message.Artifact)
            ? $"{command} {success}"
            : $"{command} {success}: {message.Artifact}";
        if (!string.IsNullOrWhiteSpace(message.Cache))
        {
            result += $" (cache {message.Cache})";
        }
        return result;
    }

    private static string FormatTestFinished(RocketMessage message)
    {
        var status = string.IsNullOrWhiteSpace(message.Status) ? "DONE" : message.Status.ToUpperInvariant();
        var name = string.IsNullOrWhiteSpace(message.Name) ? "test" : message.Name;
        return message.ExitCode.HasValue ? $"{status} {name} (exit {message.ExitCode.Value})" : $"{status} {name}";
    }

    private static DiagnosticSeverity MapSeverity(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "error" => DiagnosticSeverity.Error,
        "warning" or "warn" => DiagnosticSeverity.Warning,
        "hint" => DiagnosticSeverity.Hint,
        _ => DiagnosticSeverity.Information,
    };

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = GetString(element, propertyName);
        return value is not null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private static bool? GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
