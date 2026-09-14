using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using RocketIDE.Core.Logging;
using RocketIDE.Infrastructure.Logging;

namespace RocketIDE.App;

public partial class App : Application
{
    private readonly IApplicationLogger _logger = RotatingFileLogger.CreateDefault();
    private bool _handlingFatalDispatcherException;

    internal IApplicationLogger Logger => _logger;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        Exit += App_Exit;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // MessageBox.Show runs a nested dispatcher loop. If the main window keeps throwing
        // while that dialog is visible, DispatcherUnhandledException can re-enter and create
        // an error-dialog storm. Claim the exception immediately and ignore re-entry; the
        // first handler invocation owns logging, the single user-facing dialog, and shutdown.
        e.Handled = true;
        if (_handlingFatalDispatcherException)
        {
            return;
        }

        _handlingFatalDispatcherException = true;
        _logger.Error("Unhandled dispatcher exception.", e.Exception);
        var logPath = TryWriteCrashLog(e.Exception);
        var location = logPath is null
            ? "RocketIDE could not write a crash log."
            : $"Crash details were written to:\n{logPath}";

        MessageBox.Show(
            $"RocketIDE encountered an unexpected error and must close.\n\n{e.Exception.Message}\n\n{location}",
            "RocketIDE unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown(-1);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger.Error("Unhandled application-domain exception.", exception);
        }
    }

    private void App_Exit(object sender, ExitEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        if (_logger is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string? TryWriteCrashLog(Exception exception)
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(root, "RocketIDE", "logs");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.log");
            var text = new StringBuilder()
                .AppendLine($"UTC: {DateTime.UtcNow:O}")
                .AppendLine($"RocketIDE: {typeof(App).Assembly.GetName().Version}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($".NET: {Environment.Version}")
                .AppendLine()
                .AppendLine(RotatingFileLogger.Redact(exception.ToString()))
                .ToString();
            File.WriteAllText(path, text, Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
