using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer;

public sealed class RocketLanguageClient : IRocketLanguageClient
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private Process? _process;
    private JsonRpcConnection? _connection;
    private Task? _stderrTask;
    private int _initialized;
    private int _stopping;
    private int _faultReported;

    public bool IsInitialized => Volatile.Read(ref _initialized) != 0;
    public JsonElement? ServerCapabilities { get; private set; }

    public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived;
    public event EventHandler<RocketTransportFaultedEventArgs>? Faulted;
    public event EventHandler<string>? LogReceived;

    public async Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        if (_process is not null)
        {
            throw new InvalidOperationException("The Rocket language server is already running.");
        }

        Volatile.Write(ref _stopping, 0);
        Volatile.Write(ref _faultReported, 0);
        var fullServerPath = Path.GetFullPath(serverPath);
        var fullWorkspacePath = Path.GetFullPath(workspacePath);
        var startInfo = CreateProcessStartInfo(fullServerPath);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("rocket-lsp.exe did not start.");
            }
        }
        catch
        {
            process.Dispose();
            throw;
        }

        _process = process;
        process.Exited += Process_Exited;
        _stderrTask = PumpStderrAsync(process, CancellationToken.None);
        var connection = new JsonRpcConnection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        _connection = connection;
        connection.NotificationReceived += Connection_NotificationReceived;
        connection.Faulted += Connection_Faulted;

        try
        {
            var workspaceUri = PathToUri(fullWorkspacePath);
            var initialize = new
            {
                processId = Environment.ProcessId,
                clientInfo = new
                {
                    name = "RocketIDE",
                    version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0",
                },
                rootUri = workspaceUri,
                capabilities = new
                {
                    general = new { positionEncodings = new[] { "utf-16" } },
                    workspace = new { workspaceFolders = true },
                    textDocument = new { synchronization = new { dynamicRegistration = false, didSave = true } },
                },
                workspaceFolders = new[]
                {
                    new { uri = workspaceUri, name = Path.GetFileName(fullWorkspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) },
                },
            };

            var result = await connection.RequestAsync<InitializeResult>("initialize", initialize, cancellationToken).ConfigureAwait(false)
                ?? throw new LspProtocolException("rocket-lsp returned a null initialize result.");
            ValidateServerCapabilities(result.Capabilities);
            ServerCapabilities = result.Capabilities.Clone();
            await connection.NotifyAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _initialized, 1);
        }
        catch
        {
            Interlocked.Exchange(ref _stopping, 1);
            try
            {
                await ForceStopAsync().ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _stopping, 0);
            }
            throw;
        }
    }

    public Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        return _connection!.RequestAsync<TResponse>(method, parameters, cancellationToken);
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        return _connection!.NotifyAsync(method, parameters, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        try
        {
            var process = _process;
            var connection = _connection;
            if (process is null)
            {
                return;
            }

            if (connection is not null && IsInitialized)
            {
                using var gracefulCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                gracefulCts.CancelAfter(ShutdownTimeout);
                try
                {
                    _ = await connection.RequestAsync<JsonElement>("shutdown", null, gracefulCts.Token).ConfigureAwait(false);
                    await connection.NotifyAsync("exit", null, gracefulCts.Token).ConfigureAwait(false);
                    await process.WaitForExitAsync(gracefulCts.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is OperationCanceledException or JsonRpcResponseException or LspProtocolException or IOException or InvalidOperationException)
                {
                    RaiseLogSafely($"rocket-lsp graceful shutdown failed: {exception.Message}");
                }
            }

            await ForceStopAsync().ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _stopping, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            RaiseLogSafely($"rocket-lsp disposal failed: {exception.Message}");
            await ForceStopAsync().ConfigureAwait(false);
        }
    }

    internal static ProcessStartInfo CreateProcessStartInfo(string serverPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverPath);
        var fullServerPath = Path.GetFullPath(serverPath);
        var toolDirectory = Path.GetDirectoryName(fullServerPath)
            ?? throw new IOException($"Cannot determine the rocket-lsp directory for '{fullServerPath}'.");
        return new ProcessStartInfo
        {
            FileName = fullServerPath,
            // Never use the opened workspace as the native process current directory. Even a
            // trusted executable should not inherit an untrusted project's DLL/config search path.
            WorkingDirectory = toolDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
    }

    private static void ValidateServerCapabilities(JsonElement capabilities)
    {
        if (capabilities.ValueKind != JsonValueKind.Object)
        {
            throw new LspProtocolException("rocket-lsp initialize result did not contain a capabilities object.");
        }

        if (capabilities.TryGetProperty("positionEncoding", out var positionEncoding) &&
            (positionEncoding.ValueKind != JsonValueKind.String ||
             !string.Equals(positionEncoding.GetString(), "utf-16", StringComparison.OrdinalIgnoreCase)))
        {
            throw new LspProtocolException("RocketIDE requires rocket-lsp UTF-16 positions.");
        }

        if (!capabilities.TryGetProperty("textDocumentSync", out var sync))
        {
            throw new LspProtocolException("rocket-lsp did not advertise textDocumentSync.");
        }

        var incremental = sync.ValueKind switch
        {
            JsonValueKind.Number => sync.TryGetInt32(out var value) && value == 2,
            JsonValueKind.Object => sync.TryGetProperty("change", out var change) && change.TryGetInt32(out var value) && value == 2,
            _ => false,
        };
        if (!incremental)
        {
            throw new LspProtocolException("RocketIDE requires rocket-lsp incremental text synchronization (change = 2).");
        }
    }

    private async Task PumpStderrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                RaiseLogSafely(line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException exception)
        {
            RaiseLogSafely($"rocket-lsp stderr read failed: {exception.Message}");
        }
    }

    private void Connection_NotificationReceived(object? sender, RocketServerNotificationEventArgs e) =>
        RaiseEventSafely(NotificationReceived, e, "notification");

    private void Connection_Faulted(object? sender, RocketTransportFaultedEventArgs e)
    {
        if (Volatile.Read(ref _stopping) == 0)
        {
            Volatile.Write(ref _initialized, 0);
            ReportFault(e.Exception);
        }
    }

    private void Process_Exited(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _stopping) != 0 || sender is not Process process || !ReferenceEquals(process, _process))
        {
            return;
        }

        Volatile.Write(ref _initialized, 0);
        var exitCode = TryGetExitCode(process);
        var detail = exitCode is null ? string.Empty : $" with code {exitCode.Value}";
        ReportFault(new IOException($"rocket-lsp exited unexpectedly{detail}."));
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void ReportFault(Exception exception)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0)
        {
            return;
        }

        RaiseEventSafely(Faulted, new RocketTransportFaultedEventArgs(exception), "fault");
    }

    private void RaiseEventSafely<TEventArgs>(EventHandler<TEventArgs>? handlers, TEventArgs args, string eventName)
        where TEventArgs : EventArgs
    {
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError($"RocketIDE language-client {eventName} observer failed: {exception}");
            }
        }
    }

    private async Task ForceStopAsync()
    {
        Volatile.Write(ref _initialized, 0);
        ServerCapabilities = null;
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
        {
            connection.NotificationReceived -= Connection_NotificationReceived;
            connection.Faulted -= Connection_Faulted;
        }

        // Close the child first so a blocked read on redirected stdout is guaranteed to reach EOF
        // before the JSON-RPC reader is awaited during disposal.
        var process = Interlocked.Exchange(ref _process, null);
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                RaiseLogSafely($"rocket-lsp process cleanup failed: {exception.Message}");
            }
            finally
            {
                process.Exited -= Process_Exited;
                process.Dispose();
            }
        }

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        var stderr = Interlocked.Exchange(ref _stderrTask, null);
        if (stderr is not null)
        {
            try
            {
                await stderr.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                RaiseLogSafely($"rocket-lsp stderr cleanup failed: {exception.Message}");
            }
        }
    }


    private void RaiseLogSafely(string line)
    {
        var handlers = LogReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, line);
            }
            catch (Exception exception)
            {
                Trace.TraceError($"RocketIDE language-client log observer failed: {exception}");
            }
        }
    }

    private void EnsureInitialized()
    {
        if (!IsInitialized || _connection is null)
        {
            throw new InvalidOperationException("The Rocket language server is not initialized.");
        }
    }

    private static string PathToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private sealed record InitializeResult(JsonElement Capabilities);
}
