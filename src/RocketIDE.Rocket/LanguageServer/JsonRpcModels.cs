using System.Text.Json;

namespace RocketIDE.Rocket.LanguageServer;

public sealed class RocketServerNotificationEventArgs(string method, JsonElement parameters) : EventArgs
{
    public string Method { get; } = method;
    public JsonElement Parameters { get; } = parameters;
}

public sealed class RocketTransportFaultedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));
}

internal sealed record JsonRpcErrorPayload(int Code, string Message, JsonElement? Data);
