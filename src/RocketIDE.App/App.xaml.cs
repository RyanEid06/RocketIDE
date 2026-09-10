using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace RocketIDE.App;

public partial class App : Application
{
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = TryWriteCrashLog(e.Exception);
        var location = logPath is null
            ? "RocketIDE could not write a crash log."
            : $"Crash details were written to:\n{logPath}";

        MessageBox.Show(
            $"RocketIDE encountered an unexpected error and must close.\n\n{e.Exception.Message}\n\n{location}",
            "RocketIDE unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Do not mark the exception handled. Continuing after an unknown UI exception can
        // corrupt editor state; the log gives the next debugging pass the real stack trace.
        e.Handled = false;
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
                .AppendLine(exception.ToString())
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
