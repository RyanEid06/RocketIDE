namespace RocketIDE.Core.Logging;

public interface IApplicationLogger
{
    void Log(ApplicationLogLevel level, string message, Exception? exception = null);

    void Debug(string message) => Log(ApplicationLogLevel.Debug, message);
    void Information(string message) => Log(ApplicationLogLevel.Information, message);
    void Warning(string message, Exception? exception = null) => Log(ApplicationLogLevel.Warning, message, exception);
    void Error(string message, Exception? exception = null) => Log(ApplicationLogLevel.Error, message, exception);
}
