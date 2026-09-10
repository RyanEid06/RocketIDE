namespace RocketIDE.Rocket.LanguageServer;

public sealed class LspProtocolException : Exception
{
    public LspProtocolException(string message) : base(message) { }
    public LspProtocolException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class JsonRpcResponseException(int code, string message) : Exception($"JSON-RPC error {code}: {message}")
{
    public int Code { get; } = code;
}
