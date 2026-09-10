using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Rocket.VisualStudio.Core
{
    internal static class RocketMessageParser
    {
        internal static bool TryParse(string line, out RocketMessage message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') return false;
            try
            {
                var values = new JavaScriptSerializer().DeserializeObject(line) as IDictionary<string, object>;
                if (values == null || GetString(values, "schema") != "rocket-message-1") return false;
                message = new RocketMessage
                {
                    Reason = GetString(values, "reason"),
                    Level = GetString(values, "level"),
                    Code = GetString(values, "code"),
                    Message = GetString(values, "message"),
                    Command = GetString(values, "command"),
                    Success = GetBool(values, "success"),
                    Artifact = GetString(values, "artifact"),
                    Cache = GetString(values, "cache"),
                    Name = GetString(values, "name"),
                    Status = GetString(values, "status"),
                    ExitCode = GetInt(values, "exitCode"),
                    Passed = GetInt(values, "passed"),
                    Failed = GetInt(values, "failed"),
                    ExpectedFailures = GetInt(values, "expectedFailures"),
                    Selected = GetInt(values, "selected")
                };
                if (values.TryGetValue("span", out var spanObject) && spanObject is IDictionary<string, object> span)
                {
                    message.File = GetString(span, "file");
                    message.Line = GetInt(span, "line");
                    message.Column = GetInt(span, "column");
                }
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static string FormatForOutput(RocketMessage message)
        {
            switch (message.Reason)
            {
                case "diagnostic":
                    var location = string.IsNullOrWhiteSpace(message.File) ? string.Empty :
                        $"{message.File}({message.Line},{message.Column}): ";
                    return $"{location}{message.Level} {message.Code}: {message.Message}";
                case "build-finished":
                    if (!string.IsNullOrWhiteSpace(message.Artifact))
                        return $"{message.Command} succeeded: {message.Artifact}" +
                               (string.IsNullOrWhiteSpace(message.Cache) ? string.Empty : $" ({message.Cache})");
                    return $"{message.Command} succeeded";
                case "test-started": return $"test {message.Name}";
                case "test-finished": return $"{message.Status.ToUpperInvariant()} {message.Name} (exit {message.ExitCode})";
                case "test-summary":
                    return $"{message.Passed} passed; {message.Failed} failed; " +
                           $"{message.ExpectedFailures} expected failure(s); {message.Selected} selected";
                default: return message.Message ?? message.Reason ?? string.Empty;
            }
        }

        private static string GetString(IDictionary<string, object> values, string name)
        {
            return values.TryGetValue(name, out var value) && value != null ? Convert.ToString(value) : null;
        }

        private static int GetInt(IDictionary<string, object> values, string name)
        {
            return values.TryGetValue(name, out var value) && value != null ? Convert.ToInt32(value) : 0;
        }

        private static bool GetBool(IDictionary<string, object> values, string name)
        {
            return values.TryGetValue(name, out var value) && value != null && Convert.ToBoolean(value);
        }
    }
}
