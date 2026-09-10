using System.Text;

namespace RocketIDE.Rocket.LanguageServer;

public static class LspFrameWriter
{
    public static async Task WriteAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(json);
        var body = Encoding.UTF8.GetBytes(json);
        if (body.Length > LspFrameReader.MaxMessageBytes)
        {
            throw new LspProtocolException($"LSP message body exceeds the {LspFrameReader.MaxMessageBytes}-byte limit.");
        }

        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
