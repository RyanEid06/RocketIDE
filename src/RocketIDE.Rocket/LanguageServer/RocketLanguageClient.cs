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

    public bool IsInitialized => Volatile.Read(ref _initialized) != 0;
    public JsonElement? ServerCapabilities { get; private set; }

    public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived;
    public event EventHandler<string>? LogReceived;

    public async Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        if (_process is not null)
        {
            throw new InvalidOperationException("The Rocket language server is already running.");
        }

        var fullServerPath = Path.GetFullPath(serverPath);
        var fullWorkspacePath = Path.GetFullPath(workspacePath);
        var workingDirectory = Directory.Exists(fullWorkspacePath)
            ? fullWorkspacePath
            : Path.GetDirectoryName(fullWorkspacePath) ?? Directory.GetCurrentDirectory();
        var startInfo = new ProcessStartInfo
        {
            FileName = fullServerPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

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
        _stderrTask = PumpStderrAsync(process, CancellationToken.None);
        var connection = new JsonRpcConnection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        _connection = connection;
        connection.NotificationReceived += Connection_NotificationReceived;

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
            await ForceStopAsync().ConfigureAwait(false);
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
                LogReceived?.Invoke(this, $"rocket-lsp graceful shutdown failed: {exception.Message}");
            }
        }

        await ForceStopAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LogReceived?.Invoke(this, $"rocket-lsp disposal failed: {exception.Message}");
            await ForceStopAsync().ConfigureAwait(false);
        }
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

                LogReceived?.Invoke(this, line);
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
            LogReceived?.Invoke(this, $"rocket-lsp stderr read failed: {exception.Message}");
        }
    }

    private void Connection_NotificationReceived(object? sender, RocketServerNotificationEventArgs e) =>
        NotificationReceived?.Invoke(this, e);

    private async Task ForceStopAsync()
    {
        Volatile.Write(ref _initialized, 0);
        ServerCapabilities = null;
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
        {
            connection.NotificationReceived -= Connection_NotificationReceived;
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
                LogReceived?.Invoke(this, $"rocket-lsp process cleanup failed: {exception.Message}");
            }
            finally
            {
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
                LogReceived?.Invoke(this, $"rocket-lsp stderr cleanup failed: {exception.Message}");
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
