using System.Globalization;
using System.Text;

namespace RocketIDE.Rocket.LanguageServer;

public static class LspFrameReader
{
    public const int MaxHeaderBytes = 16 * 1024;
    public const int MaxMessageBytes = 16 * 1024 * 1024;

    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = await ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);
        var length = ParseContentLength(header);
        if (length > MaxMessageBytes)
        {
            throw new LspProtocolException($"LSP message body exceeds the {MaxMessageBytes}-byte limit.");
        }

        var body = GC.AllocateUninitializedArray<byte>(length);
        var offset = 0;
        while (offset < body.Length)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("The LSP stream ended before the declared message body was complete.");
            }

            offset += read;
        }

        return body;
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(capacity: 256);
        var oneByte = new byte[1];
        var matched = 0;
        byte[] terminator = [13, 10, 13, 10];

        while (true)
        {
            var read = await stream.ReadAsync(oneByte.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("The LSP stream ended before a complete header was received.");
            }

            buffer.WriteByte(oneByte[0]);
            if (buffer.Length > MaxHeaderBytes)
            {
                throw new LspProtocolException($"LSP header exceeds the {MaxHeaderBytes}-byte limit.");
            }

            if (oneByte[0] == terminator[matched])
            {
                matched++;
                if (matched == terminator.Length)
                {
                    return buffer.ToArray();
                }
            }
            else
            {
                matched = oneByte[0] == terminator[0] ? 1 : 0;
            }
        }
    }

    private static int ParseContentLength(byte[] headerBytes)
    {
        foreach (var value in headerBytes)
        {
            if (value > 0x7F)
            {
                throw new LspProtocolException("LSP header is not valid ASCII.");
            }
        }

        var header = Encoding.ASCII.GetString(headerBytes);

        int? contentLength = null;
        foreach (var line in header.Split("\r\n", StringSplitOptions.None))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new LspProtocolException("Malformed LSP header line.");
            }

            var name = line[..separator].Trim();
            if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (contentLength is not null)
            {
                throw new LspProtocolException("Duplicate Content-Length header.");
            }

            var value = line[(separator + 1)..].Trim();
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            {
                throw new LspProtocolException("Invalid Content-Length header.");
            }

            contentLength = parsed;
        }

        return contentLength ?? throw new LspProtocolException("Missing Content-Length header.");
    }
}
