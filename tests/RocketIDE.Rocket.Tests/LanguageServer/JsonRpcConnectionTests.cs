using System.Text.Json;
using System.Threading.Channels;
using RocketIDE.Rocket.LanguageServer;

namespace RocketIDE.Rocket.Tests.LanguageServer;

[TestClass]
public sealed class JsonRpcConnectionTests
{
    [TestMethod]
    public async Task RequestAsync_IncrementsIdsAndCorrelatesOutOfOrderResponses()
    {
        await using var serverToClient = new AsyncByteStream();
        await using var clientToServer = new AsyncByteStream();
        await using var connection = new JsonRpcConnection(serverToClient, clientToServer);

        var firstTask = connection.RequestAsync<TestResult>("first", new { value = 1 }, CancellationToken.None);
        var secondTask = connection.RequestAsync<TestResult>("second", new { value = 2 }, CancellationToken.None);
        var firstRequest = await ReadJsonAsync(clientToServer);
        var secondRequest = await ReadJsonAsync(clientToServer);
        var firstId = firstRequest.GetProperty("id").GetInt64();
        var secondId = secondRequest.GetProperty("id").GetInt64();

        Assert.AreEqual(firstId + 1, secondId);
        await WriteJsonAsync(serverToClient, new { jsonrpc = "2.0", id = secondId, result = new { value = 22 } });
        await WriteJsonAsync(serverToClient, new { jsonrpc = "2.0", id = firstId, result = new { value = 11 } });

        Assert.AreEqual(11, (await firstTask)!.Value);
        Assert.AreEqual(22, (await secondTask)!.Value);
    }

    [TestMethod]
    public async Task NotifyAsync_WritesNotificationWithoutId()
    {
        await using var serverToClient = new AsyncByteStream();
        await using var clientToServer = new AsyncByteStream();
        await using var connection = new JsonRpcConnection(serverToClient, clientToServer);

        await connection.NotifyAsync("initialized", new { }, CancellationToken.None);
        var message = await ReadJsonAsync(clientToServer);

        Assert.AreEqual("initialized", message.GetProperty("method").GetString());
        Assert.IsFalse(message.TryGetProperty("id", out _));
    }

    [TestMethod]
    public async Task RequestAsync_MapsJsonRpcErrorAndIgnoresUnknownResponseId()
    {
        await using var serverToClient = new AsyncByteStream();
        await using var clientToServer = new AsyncByteStream();
        await using var connection = new JsonRpcConnection(serverToClient, clientToServer);

        var requestTask = connection.RequestAsync<TestResult>("boom", null, CancellationToken.None);
        var request = await ReadJsonAsync(clientToServer);
        var id = request.GetProperty("id").GetInt64();
        await WriteJsonAsync(serverToClient, new { jsonrpc = "2.0", id = id + 999, result = new { value = 1 } });
        await WriteJsonAsync(serverToClient, new { jsonrpc = "2.0", id, error = new { code = -32800, message = "request cancelled" } });

        var exception = await Assert.ThrowsExactlyAsync<JsonRpcResponseException>(async () => _ = await requestTask);
        Assert.AreEqual(-32800, exception.Code);
    }

    [TestMethod]
    public async Task RequestAsync_CancellationCancelsPendingTaskAndSendsCancelNotification()
    {
        await using var serverToClient = new AsyncByteStream();
        await using var clientToServer = new AsyncByteStream();
        await using var connection = new JsonRpcConnection(serverToClient, clientToServer);
        using var cts = new CancellationTokenSource();

        var requestTask = connection.RequestAsync<TestResult>("slow", null, cts.Token);
        var request = await ReadJsonAsync(clientToServer);
        var id = request.GetProperty("id").GetInt64();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => _ = await requestTask);
        var cancel = await ReadJsonAsync(clientToServer);
        Assert.AreEqual("$/cancelRequest", cancel.GetProperty("method").GetString());
        Assert.AreEqual(id, cancel.GetProperty("params").GetProperty("id").GetInt64());
    }

    [TestMethod]
    public async Task ReaderLoop_DispatchesServerNotification()
    {
        await using var serverToClient = new AsyncByteStream();
        await using var clientToServer = new AsyncByteStream();
        await using var connection = new JsonRpcConnection(serverToClient, clientToServer);
        var completion = new TaskCompletionSource<RocketServerNotificationEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.NotificationReceived += (_, args) => completion.TrySetResult(args);

        await WriteJsonAsync(serverToClient, new { jsonrpc = "2.0", method = "rocket/analysisStatus", @params = new { files = 3 } });
        var notification = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual("rocket/analysisStatus", notification.Method);
        Assert.AreEqual(3, notification.Parameters.GetProperty("files").GetInt32());
    }


    [TestMethod]
    public async Task NotificationSubscriberException_DoesNotKillReaderLoopOrOtherSubscribers()
    {
        await using var serverToClient = new AsyncByteStream();
        await using var clientToServer = new AsyncByteStream();
        await using var connection = new JsonRpcConnection(serverToClient, clientToServer);
        var notifications = 0;
        connection.NotificationReceived += (_, _) => throw new InvalidOperationException("observer failed");
        connection.NotificationReceived += (_, _) => Interlocked.Increment(ref notifications);

        await WriteJsonAsync(serverToClient, new { jsonrpc = "2.0", method = "rocket/analysisStatus", @params = new { files = 1 } });
        await WaitUntilAsync(() => Volatile.Read(ref notifications) == 1);

        var requestTask = connection.RequestAsync<TestResult>("still-alive", null, CancellationToken.None);
        var request = await ReadJsonAsync(clientToServer);
        var id = request.GetProperty("id").GetInt64();
        await WriteJsonAsync(serverToClient, new { jsonrpc = "2.0", id, result = new { value = 42 } });

        Assert.AreEqual(42, (await requestTask)!.Value);
    }

    [TestMethod]
    public async Task UnexpectedInputEof_RaisesFaultAndFailsPendingRequest()
    {
        await using var serverToClient = new AsyncByteStream();
        await using var clientToServer = new AsyncByteStream();
        await using var connection = new JsonRpcConnection(serverToClient, clientToServer);
        var fault = new TaskCompletionSource<RocketTransportFaultedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += (_, args) => fault.TrySetResult(args);

        var requestTask = connection.RequestAsync<TestResult>("pending", null, CancellationToken.None);
        _ = await ReadJsonAsync(clientToServer);
        await serverToClient.DisposeAsync();

        var observed = await fault.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsInstanceOfType<EndOfStreamException>(observed.Exception);
        await Assert.ThrowsExactlyAsync<EndOfStreamException>(async () => _ = await requestTask);
        Assert.ThrowsExactly<LspProtocolException>(() =>
        {
            _ = connection.NotifyAsync("after-fault", null, CancellationToken.None);
        });
        await Assert.ThrowsExactlyAsync<LspProtocolException>(
            async () => _ = await connection.RequestAsync<TestResult>("after-fault", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task RequestAsync_CancellationDuringFrameWrite_CompletesFrameBeforeCancelingRequest()
    {
        await using var serverToClient = new AsyncByteStream();
        await using var clientToServer = new PausingWriteStream();
        await using var connection = new JsonRpcConnection(serverToClient, clientToServer);
        using var cts = new CancellationTokenSource();

        var requestTask = connection.RequestAsync<TestResult>(
            "textDocument/completion",
            new { value = 1 },
            cts.Token);
        await clientToServer.BodyWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

        cts.Cancel();
        try
        {
            Assert.IsFalse(
                clientToServer.BodyWriteToken.IsCancellationRequested,
                "Canceling a request must not cancel the stream write after an LSP frame header has been written.");
        }
        finally
        {
            clientToServer.ReleaseBodyWrite();
        }

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => _ = await requestTask.WaitAsync(TimeSpan.FromSeconds(2)));

        using var snapshot = new MemoryStream(clientToServer.Snapshot());
        var request = await ReadJsonAsync(snapshot);
        Assert.AreEqual("textDocument/completion", request.GetProperty("method").GetString());
        Assert.AreEqual(JsonValueKind.Number, request.GetProperty("id").ValueKind);
    }

    [TestMethod]
    public async Task MalformedResponse_FailsTheMatchingPendingRequestInsteadOfLeavingItHung()
    {
        await using var serverToClient = new AsyncByteStream();
        await using var clientToServer = new AsyncByteStream();
        await using var connection = new JsonRpcConnection(serverToClient, clientToServer);

        var requestTask = connection.RequestAsync<TestResult>("bad-response", null, CancellationToken.None);
        var request = await ReadJsonAsync(clientToServer);
        var id = request.GetProperty("id").GetInt64();
        await WriteJsonAsync(serverToClient, new { jsonrpc = "2.0", id });

        await Assert.ThrowsExactlyAsync<LspProtocolException>(async () => _ = await requestTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition());
    }

    private static async Task<JsonElement> ReadJsonAsync(Stream stream)
    {
        var body = await LspFrameReader.ReadAsync(stream, CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static Task WriteJsonAsync(Stream stream, object value) =>
        LspFrameWriter.WriteAsync(stream, JsonSerializer.Serialize(value), CancellationToken.None);

    private sealed record TestResult(int Value);

    private sealed class PausingWriteStream : Stream
    {
        private readonly MemoryStream _buffer = new();
        private readonly object _sync = new();
        private readonly TaskCompletionSource<bool> _bodyWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowBodyWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writeCount;
        private CancellationToken _bodyWriteToken;
        private bool _disposed;

        public Task BodyWriteStarted => _bodyWriteStarted.Task;
        public CancellationToken BodyWriteToken => _bodyWriteToken;

        public void ReleaseBodyWrite() => _allowBodyWrite.TrySetResult(true);

        public byte[] Snapshot()
        {
            lock (_sync)
            {
                return _buffer.ToArray();
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var write = Interlocked.Increment(ref _writeCount);
            if (write == 2)
            {
                _bodyWriteToken = cancellationToken;
                _bodyWriteStarted.TrySetResult(true);
                await _allowBodyWrite.Task.WaitAsync(cancellationToken);
            }

            lock (_sync)
            {
                _buffer.Write(buffer.Span);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _allowBodyWrite.TrySetResult(true);
                _buffer.Dispose();
            }
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncByteStream : Stream
    {
        private readonly Channel<byte> _channel = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        private bool _disposed;

        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0) return 0;
            try
            {
                var first = await _channel.Reader.ReadAsync(cancellationToken);
                buffer.Span[0] = first;
                var count = 1;
                while (count < buffer.Length && _channel.Reader.TryRead(out var value))
                {
                    buffer.Span[count++] = value;
                }
                return count;
            }
            catch (ChannelClosedException)
            {
                return 0;
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            foreach (var value in buffer.Span)
            {
                if (!_channel.Writer.TryWrite(value))
                {
                    throw new IOException("The in-memory protocol stream is closed.");
                }
            }
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _channel.Writer.TryComplete();
            }
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
