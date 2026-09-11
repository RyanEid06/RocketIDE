using System.Text.Json;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class SemanticTokensClientTests
{
    [TestMethod]
    public async Task RequestAsync_InvalidDeltaFallsBackToFullRequest()
    {
        var client = new QueueLanguageClient(
            JsonDocument.Parse("""{ "resultId": "r1", "data": [0, 0, 2, 0, 0] }""").RootElement.Clone(),
            JsonDocument.Parse("""{ "resultId": "bad", "edits": [{ "start": 99, "deleteCount": 0 }] }""").RootElement.Clone(),
            JsonDocument.Parse("""{ "resultId": "r2", "data": [0, 0, 4, 0, 0] }""").RootElement.Clone());
        var feature = new SemanticTokensClient(
            client,
            new SemanticTokenLegend(new[] { "keyword" }, Array.Empty<string>()),
            supportsDelta: true);

        var first = await feature.RequestAsync("C:/workspace/main.rocket", CancellationToken.None);
        var second = await feature.RequestAsync("C:/workspace/main.rocket", CancellationToken.None);

        Assert.AreEqual("r1", first?.ResultId);
        Assert.AreEqual("r2", second?.ResultId);
        CollectionAssert.AreEqual(
            new[] { "textDocument/semanticTokens/full", "textDocument/semanticTokens/full/delta", "textDocument/semanticTokens/full" },
            client.Methods.ToArray());
    }

    [TestMethod]
    public void Decode_UsesRelativeLspPositionsAndLegend()
    {
        var legend = new SemanticTokenLegend(
            new[] { "keyword", "function", "parameter" },
            new[] { "declaration", "readonly" });
        int[] data =
        [
            0, 0, 2, 0, 0,
            0, 3, 4, 1, 1,
            2, 1, 5, 2, 2,
        ];

        var tokens = SemanticTokensClient.Decode(data, legend);

        Assert.AreEqual(3, tokens.Count);
        Assert.AreEqual((0, 0, 2, "keyword"), (tokens[0].Line, tokens[0].Character, tokens[0].Length, tokens[0].TokenType));
        Assert.AreEqual((0, 3, 4, "function"), (tokens[1].Line, tokens[1].Character, tokens[1].Length, tokens[1].TokenType));
        CollectionAssert.AreEqual(new[] { "declaration" }, tokens[1].Modifiers.ToArray());
        Assert.AreEqual((2, 1, 5, "parameter"), (tokens[2].Line, tokens[2].Character, tokens[2].Length, tokens[2].TokenType));
        CollectionAssert.AreEqual(new[] { "readonly" }, tokens[2].Modifiers.ToArray());
    }

    [TestMethod]
    public void ApplyDelta_ReplacesEncodedTokenSlice()
    {
        int[] previous = [0, 0, 2, 0, 0, 0, 3, 4, 1, 0];
        var edits = new[]
        {
            new SemanticTokensEdit(5, 5, new[] { 1, 1, 6, 2, 0 }),
        };

        var updated = SemanticTokensClient.ApplyDelta(previous, edits);

        CollectionAssert.AreEqual(new[] { 0, 0, 2, 0, 0, 1, 1, 6, 2, 0 }, updated.ToArray());
    }

    [TestMethod]
    public void ApplyDelta_RejectsOutOfBoundsCacheEdit()
    {
        int[] previous = [0, 0, 2, 0, 0];
        var edits = new[] { new SemanticTokensEdit(6, 0, Array.Empty<int>()) };

        Assert.ThrowsExactly<LspProtocolException>(() => SemanticTokensClient.ApplyDelta(previous, edits));
    }

    private sealed class QueueLanguageClient(params JsonElement[] responses) : IRocketLanguageClient
    {
        private readonly Queue<JsonElement> _responses = new(responses);
        public List<string> Methods { get; } = [];
        public bool IsInitialized => true;
        public RocketLanguageServerCapabilities Capabilities => RocketLanguageServerCapabilities.None;

        public event EventHandler<RocketServerNotificationEventArgs>? NotificationReceived { add { } remove { } }
        public event EventHandler<RocketTransportFaultedEventArgs>? Faulted { add { } remove { } }
        public event EventHandler<string>? LogReceived { add { } remove { } }

        public Task StartAsync(string serverPath, string workspacePath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TResponse?> RequestAsync<TResponse>(string method, object? parameters, CancellationToken cancellationToken)
        {
            Methods.Add(method);
            object response = _responses.Dequeue();
            return Task.FromResult(response is TResponse typed ? typed : default);
        }

        public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
