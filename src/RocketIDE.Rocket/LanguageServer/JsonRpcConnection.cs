using System.Collections.Concurrent;
using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer;

public sealed class JsonRpcConnection : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _readerTask;
    private long _nextRequestId;
    private int _disposed;

    public JsonRpcConnection(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        _input = input;
        _output = output;
        _readerTask = Task.Run(ReadLoopAsync);
    }

    public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived;

    public async Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException($"Duplicate JSON-RPC request id {id}.");
        }

        try
        {
            await WriteMessageAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        using var registration = cancellationToken.Register(() => CancelRequest(id, cancellationToken));
        var result = await completion.Task.ConfigureAwait(false);
        if (result is null || result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return default;
        }

        try
        {
            return result.Value.Deserialize<TResponse>(LspJson.Options);
        }
        catch (JsonException exception)
        {
            throw new LspProtocolException($"JSON-RPC result for '{method}' could not be deserialized as {typeof(TResponse).Name}.", exception);
        }
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ThrowIfDisposed();
        return WriteMessageAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellationToken);
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            while (!_disposeCts.IsCancellationRequested)
            {
                var body = await LspFrameReader.ReadAsync(_input, _disposeCts.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(body);
                await HandleIncomingAsync(document.RootElement).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or JsonException or LspProtocolException or ObjectDisposedException)
        {
            failure = exception;
        }
        finally
        {
            var terminal = failure ?? new ObjectDisposedException(nameof(JsonRpcConnection));
            foreach (var entry in _pending.ToArray())
            {
                if (_pending.TryRemove(entry.Key, out var pending))
                {
                    pending.TrySetException(terminal);
                }
            }
        }
    }

    private async Task HandleIncomingAsync(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("jsonrpc", out var version) ||
            version.ValueKind != JsonValueKind.String ||
            version.GetString() != "2.0")
        {
            throw new LspProtocolException("Incoming message is not a valid JSON-RPC 2.0 object.");
        }

        var hasMethod = root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String;
        var hasId = root.TryGetProperty("id", out var idElement);
        if (hasMethod)
        {
            var method = methodElement.GetString()!;
            if (hasId)
            {
                await RespondMethodNotFoundAsync(idElement).ConfigureAwait(false);
                return;
            }

            var parameters = root.TryGetProperty("params", out var paramsElement)
                ? paramsElement.Clone()
                : JsonSerializer.SerializeToElement<object?>(null, LspJson.Options);
            NotificationReceived?.Invoke(this, new RocketServerNotificationEventArgs(method, parameters));
            return;
        }

        if (!hasId || idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt64(out var id))
        {
            throw new LspProtocolException("JSON-RPC response is missing a numeric request id.");
        }

        if (!_pending.TryRemove(id, out var completion))
        {
            return;
        }

        var hasResult = root.TryGetProperty("result", out var resultElement);
        var hasError = root.TryGetProperty("error", out var errorElement);
        if (hasResult == hasError)
        {
            completion.TrySetException(new LspProtocolException("JSON-RPC response must contain exactly one of result or error."));
            return;
        }

        if (hasError)
        {
            if (errorElement.ValueKind != JsonValueKind.Object ||
                !errorElement.TryGetProperty("code", out var codeElement) || !codeElement.TryGetInt32(out var code) ||
                !errorElement.TryGetProperty("message", out var messageElement) || messageElement.ValueKind != JsonValueKind.String)
            {
                completion.TrySetException(new LspProtocolException("Malformed JSON-RPC error response."));
                return;
            }

            completion.TrySetException(new JsonRpcResponseException(code, messageElement.GetString()!));
            return;
        }

        completion.TrySetResult(resultElement.Clone());
    }

    private async Task RespondMethodNotFoundAsync(JsonElement id)
    {
        object idValue = id.ValueKind switch
        {
            JsonValueKind.Number when id.TryGetInt64(out var number) => number,
            JsonValueKind.String => id.GetString()!,
            _ => throw new LspProtocolException("Server request has an invalid JSON-RPC id."),
        };
        await WriteMessageAsync(
            new { jsonrpc = "2.0", id = idValue, error = new { code = -32601, message = "Method not found" } },
            _disposeCts.Token).ConfigureAwait(false);
    }

    private void CancelRequest(long id, CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove(id, out var completion))
        {
            return;
        }

        completion.TrySetCanceled(cancellationToken);
        _ = SendCancellationBestEffortAsync(id);
    }

    private async Task SendCancellationBestEffortAsync(long id)
    {
        try
        {
            await WriteMessageAsync(new { jsonrpc = "2.0", method = "$/cancelRequest", @params = new { id } }, _disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException or LspProtocolException)
        {
        }
    }

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, LspJson.Options);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LspFrameWriter.WriteAsync(_output, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCts.Cancel();
        try
        {
            await _readerTask.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
        _disposeCts.Dispose();
        _writeLock.Dispose();
    }
}
