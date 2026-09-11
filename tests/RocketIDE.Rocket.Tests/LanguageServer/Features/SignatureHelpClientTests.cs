using System.Text.Json;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.LspDtos;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class SignatureHelpClientTests
{
    [TestMethod]
    public async Task RequestAsync_RetriggerCharacterUsesTriggerCharacterKind()
    {
        var client = new RecordingLanguageClient
        {
            Response = JsonDocument.Parse("""{ "signatures": [{ "label": "f(x)" }] }""").RootElement.Clone(),
        };
        var feature = new SignatureHelpClient(client);

        await feature.RequestAsync(
            "C:/workspace/main.rocket",
            new LspPosition(0, 3),
            triggerCharacter: ",",
            isRetrigger: true,
            CancellationToken.None);

        Assert.AreEqual("textDocument/signatureHelp", client.LastMethod);
        Assert.IsNotNull(client.LastParameters);
        var parameters = JsonSerializer.SerializeToElement(client.LastParameters);
        Assert.AreEqual(2, parameters.GetProperty("context").GetProperty("triggerKind").GetInt32());
        Assert.IsTrue(parameters.GetProperty("context").GetProperty("isRetrigger").GetBoolean());
    }

    [TestMethod]
    public void ParseResponse_SignatureActiveParameterOverridesTopLevelValue()
    {
        using var document = JsonDocument.Parse("""
            {
              "signatures": [
                {
                  "label": "f(first: Int, second: Int)",
                  "parameters": [
                    { "label": "first: Int" },
                    { "label": "second: Int" }
                  ],
                  "activeParameter": 1
                }
              ],
              "activeSignature": 0,
              "activeParameter": 0
            }
            """);

        var help = SignatureHelpClient.ParseResponse(document.RootElement);

        Assert.IsNotNull(help);
        Assert.AreEqual(1, help.ActiveParameter);
    }

    [TestMethod]
    public void ParseResponse_PreservesServerSignatureAndActiveParameterMetadata()
    {
        using var document = JsonDocument.Parse("""
            {
              "signatures": [
                {
                  "label": "draw_rect(x: Float, y: Float = 0)",
                  "documentation": { "kind": "markdown", "value": "Draw a rectangle." },
                  "parameters": [
                    { "label": [10, 18], "documentation": "horizontal position" },
                    { "label": "y: Float = 0", "documentation": "vertical position" }
                  ],
                  "activeParameter": 1
                }
              ],
              "activeSignature": 0,
              "activeParameter": 1
            }
            """);

        var help = SignatureHelpClient.ParseResponse(document.RootElement);

        Assert.IsNotNull(help);
        Assert.AreEqual(0, help.ActiveSignature);
        Assert.AreEqual(1, help.ActiveParameter);
        Assert.AreEqual("draw_rect(x: Float, y: Float = 0)", help.Signatures[0].Label);
        Assert.AreEqual(2, help.Signatures[0].Parameters.Count);
        Assert.AreEqual(10, help.Signatures[0].Parameters[0].LabelStart);
        Assert.AreEqual(18, help.Signatures[0].Parameters[0].LabelEnd);
        Assert.AreEqual("y: Float = 0", help.Signatures[0].Parameters[1].Label);
    }

    private sealed class RecordingLanguageClient : IRocketLanguageClient
    {
        public bool IsInitialized => true;
        public RocketLanguageServerCapabilities Capabilities => RocketLanguageServerCapabilities.None;
        public JsonElement? Response { get; init; }
        public string? LastMethod { get; private set; }
        public object? LastParameters { get; private set; }

        public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived { add { } remove { } }
        public event EventHandler<RocketTransportFaultedEventArgs>? Faulted { add { } remove { } }
        public event EventHandler<string>? LogReceived { add { } remove { } }

        public Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken)
        {
            LastMethod = method;
            LastParameters = parameters;
            object? response = Response;
            return Task.FromResult(response is TResponse typed ? typed : default);
        }

        public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
