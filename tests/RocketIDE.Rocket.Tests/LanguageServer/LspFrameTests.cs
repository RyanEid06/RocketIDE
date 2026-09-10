using System.Text;
using RocketIDE.Rocket.LanguageServer;

namespace RocketIDE.Rocket.Tests.LanguageServer;

[TestClass]
public sealed class LspFrameTests
{
    [TestMethod]
    public async Task ReadAsync_HandlesFragmentedHeadersBodiesAndBackToBackFrames()
    {
        var firstJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}";
        var secondJson = "{\"jsonrpc\":\"2.0\",\"method\":\"x\"}";
        var bytes = Encode(firstJson).Concat(Encode(secondJson)).ToArray();
        await using var stream = new FragmentedReadStream(bytes, maxChunkSize: 3);

        var first = await LspFrameReader.ReadAsync(stream, CancellationToken.None);
        var second = await LspFrameReader.ReadAsync(stream, CancellationToken.None);

        Assert.AreEqual(firstJson, Encoding.UTF8.GetString(first));
        Assert.AreEqual(secondJson, Encoding.UTF8.GetString(second));
    }

    [TestMethod]
    public async Task ReadAsync_RejectsDuplicateInvalidAndOversizedHeaders()
    {
        await Assert.ThrowsExactlyAsync<LspProtocolException>(async () =>
        {
            await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 1\r\nContent-Length: 1\r\n\r\n{}"));
            _ = await LspFrameReader.ReadAsync(stream, CancellationToken.None);
        });

        await Assert.ThrowsExactlyAsync<LspProtocolException>(async () =>
        {
            await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: nope\r\n\r\n{}"));
            _ = await LspFrameReader.ReadAsync(stream, CancellationToken.None);
        });

        var oversizedHeader = "X-Test: " + new string('a', LspFrameReader.MaxHeaderBytes) + "\r\nContent-Length: 0\r\n\r\n";
        await Assert.ThrowsExactlyAsync<LspProtocolException>(async () =>
        {
            await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(oversizedHeader));
            _ = await LspFrameReader.ReadAsync(stream, CancellationToken.None);
        });
    }

    [TestMethod]
    public async Task ReadAsync_RejectsMissingContentLengthAndNonAsciiHeaders()
    {
        await Assert.ThrowsExactlyAsync<LspProtocolException>(async () =>
        {
            await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("X-Test: 1\r\n\r\n"));
            _ = await LspFrameReader.ReadAsync(stream, CancellationToken.None);
        });

        var nonAsciiHeader = Encoding.ASCII.GetBytes("Content-Length: 0\r\nX-Test: \r\n\r\n").ToList();
        nonAsciiHeader.Insert(nonAsciiHeader.Count - 4, 0xFF);
        await Assert.ThrowsExactlyAsync<LspProtocolException>(async () =>
        {
            await using var stream = new MemoryStream(nonAsciiHeader.ToArray());
            _ = await LspFrameReader.ReadAsync(stream, CancellationToken.None);
        });
    }

    [TestMethod]
    public async Task ReadAsync_RejectsBodyOver16MiBAndPrematureEof()
    {
        var oversized = Encoding.ASCII.GetBytes($"Content-Length: {LspFrameReader.MaxMessageBytes + 1}\r\n\r\n");
        await Assert.ThrowsExactlyAsync<LspProtocolException>(async () =>
        {
            await using var stream = new MemoryStream(oversized);
            _ = await LspFrameReader.ReadAsync(stream, CancellationToken.None);
        });

        var shortBody = Encoding.ASCII.GetBytes("Content-Length: 5\r\n\r\nabc");
        await Assert.ThrowsExactlyAsync<EndOfStreamException>(async () =>
        {
            await using var stream = new MemoryStream(shortBody);
            _ = await LspFrameReader.ReadAsync(stream, CancellationToken.None);
        });
    }

    [TestMethod]
    public async Task WriteAsync_UsesUtf8ByteLengthNotCharacterCount()
    {
        const string json = "{\"value\":\"🚀\"}";
        await using var stream = new MemoryStream();

        await LspFrameWriter.WriteAsync(stream, json, CancellationToken.None);
        var wire = stream.ToArray();
        var text = Encoding.UTF8.GetString(wire);

        Assert.IsTrue(text.StartsWith($"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n", StringComparison.Ordinal));
    }

    private static byte[] Encode(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        return header.Concat(body).ToArray();
    }

    private sealed class FragmentedReadStream(byte[] data, int maxChunkSize) : Stream
    {
        private int _offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _offset; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset >= data.Length) return ValueTask.FromResult(0);
            var count = Math.Min(Math.Min(maxChunkSize, buffer.Length), data.Length - _offset);
            data.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return ValueTask.FromResult(count);
        }
    }
}
