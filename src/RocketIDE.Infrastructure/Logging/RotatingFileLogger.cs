using System.Text;
using System.Text.RegularExpressions;
using RocketIDE.Core.Logging;

namespace RocketIDE.Infrastructure.Logging;

public sealed class RotatingFileLogger : IApplicationLogger, IDisposable
{
    private static readonly Regex SecretPattern = new(
        @"(?<key>password|passwd|token|secret|api[-_]?key|authorization)(?<separator>\s*[:=]\s*)(?<value>[^\s,;]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private bool _disposed;

    public RotatingFileLogger(string path, long maxBytes = 2 * 1024 * 1024, int maxFiles = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (maxFiles < 1) throw new ArgumentOutOfRangeException(nameof(maxFiles));
        _path = Path.GetFullPath(path);
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
    }

    public static RotatingFileLogger CreateDefault()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new RotatingFileLogger(Path.Combine(root, "RocketIDE", "logs", "rocketide.log"));
    }

    public void Log(ApplicationLogLevel level, string message, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                var line = $"{DateTimeOffset.UtcNow:O} [{level}] {Redact(message)}";
                if (exception is not null)
                {
                    line += $" | {exception.GetType().Name}: {Redact(exception.Message)}";
                }
                line += Environment.NewLine;

                var directory = Path.GetDirectoryName(_path)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(_path) && new FileInfo(_path).Length + Encoding.UTF8.GetByteCount(line) > _maxBytes)
                {
                    Rotate();
                }
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch
            {
                // Logging must never take down the editor or mask the original failure.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
    }

    public static string Redact(string value) => SecretPattern.Replace(value, "${key}${separator}<redacted>");

    private void Rotate()
    {
        for (var index = _maxFiles - 1; index >= 1; index--)
        {
            var source = $"{_path}.{index}";
            var destination = $"{_path}.{index + 1}";
            if (File.Exists(destination)) File.Delete(destination);
            if (File.Exists(source)) File.Move(source, destination);
        }

        if (File.Exists(_path)) File.Move(_path, $"{_path}.1", overwrite: true);
    }
}
