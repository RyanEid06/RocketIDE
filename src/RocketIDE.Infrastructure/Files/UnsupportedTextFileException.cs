namespace RocketIDE.Infrastructure.Files;

public sealed class UnsupportedTextFileException : IOException
{
    public UnsupportedTextFileException(string path, string reason, Exception? innerException = null)
        : base($"Cannot open '{path}' as a UTF-8 text document: {reason}", innerException)
    {
        Path = path;
        Reason = reason;
    }

    public string Path { get; }

    public string Reason { get; }
}
