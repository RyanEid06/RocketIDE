using System.Collections.Concurrent;
using System.Text.Json;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record RocketSemanticToken(
    int Line,
    int Character,
    int Length,
    string TokenType,
    IReadOnlyList<string> Modifiers);

public sealed record SemanticTokensEdit(int Start, int DeleteCount, IReadOnlyList<int> Data);

public sealed record RocketSemanticTokensResult(
    string? ResultId,
    IReadOnlyList<int> EncodedData,
    IReadOnlyList<RocketSemanticToken> Tokens);

public sealed class SemanticTokensClient
{
    private readonly IRocketLanguageClient _client;
    private readonly SemanticTokenLegend _legend;
    private readonly bool _supportsDelta;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public SemanticTokensClient(IRocketLanguageClient client, SemanticTokenLegend legend, bool supportsDelta)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _legend = legend ?? throw new ArgumentNullException(nameof(legend));
        _supportsDelta = supportsDelta;
    }

    public async Task<RocketSemanticTokensResult?> RequestAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RequestCoreAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<RocketSemanticTokensResult?> RequestCoreAsync(string path, CancellationToken cancellationToken)
    {
        var uri = LspFeatureParsing.PathToUri(path);
        if (_supportsDelta && _cache.TryGetValue(uri, out var cached) && !string.IsNullOrEmpty(cached.ResultId))
        {
            try
            {
                var deltaResponse = await _client.RequestAsync<JsonElement>(
                    "textDocument/semanticTokens/full/delta",
                    new { textDocument = new { uri }, previousResultId = cached.ResultId },
                    cancellationToken).ConfigureAwait(false);
                if (deltaResponse.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    var updated = ParseDeltaOrFull(deltaResponse, cached.Data);
                    Store(uri, updated);
                    return updated;
                }
            }
            catch (Exception exception) when (exception is JsonRpcResponseException or LspProtocolException)
            {
                _cache.TryRemove(uri, out _);
            }
        }

        var fullResponse = await _client.RequestAsync<JsonElement>(
            "textDocument/semanticTokens/full",
            new { textDocument = new { uri } },
            cancellationToken).ConfigureAwait(false);
        if (fullResponse.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            _cache.TryRemove(uri, out _);
            return null;
        }

        var full = ParseFull(fullResponse);
        Store(uri, full);
        return full;
    }

    public void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        _cache.TryRemove(LspFeatureParsing.PathToUri(path), out _);
    }

    internal static IReadOnlyList<RocketSemanticToken> Decode(IReadOnlyList<int> data, SemanticTokenLegend legend)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(legend);
        if (data.Count % 5 != 0)
        {
            throw new LspProtocolException("Semantic token data length is not divisible by five.");
        }

        var tokens = new List<RocketSemanticToken>(data.Count / 5);
        var line = 0;
        var character = 0;
        for (var index = 0; index < data.Count; index += 5)
        {
            var deltaLine = data[index];
            var deltaStart = data[index + 1];
            var length = data[index + 2];
            var tokenType = data[index + 3];
            var modifierBits = data[index + 4];
            if (deltaLine < 0 || deltaStart < 0 || length <= 0 || tokenType < 0 || modifierBits < 0)
            {
                throw new LspProtocolException("Semantic token data contains a negative or zero-invalid value.");
            }
            if (tokenType >= legend.TokenTypes.Count)
            {
                throw new LspProtocolException("Semantic token type index exceeds the server legend.");
            }

            if (deltaLine == 0)
            {
                character += deltaStart;
            }
            else
            {
                line += deltaLine;
                character = deltaStart;
            }

            var modifiers = new List<string>();
            for (var bit = 0; bit < legend.TokenModifiers.Count && bit < 31; bit++)
            {
                if ((modifierBits & (1 << bit)) != 0)
                {
                    modifiers.Add(legend.TokenModifiers[bit]);
                }
            }
            tokens.Add(new RocketSemanticToken(line, character, length, legend.TokenTypes[tokenType], modifiers));
        }
        return tokens;
    }

    internal static IReadOnlyList<int> ApplyDelta(IReadOnlyList<int> previous, IReadOnlyList<SemanticTokensEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(edits);
        var normalized = edits.OrderByDescending(edit => edit.Start).ToArray();
        var lastStart = previous.Count;
        foreach (var edit in normalized)
        {
            if (edit.Start < 0 || edit.DeleteCount < 0 || edit.Start > previous.Count || edit.Start + edit.DeleteCount > previous.Count)
            {
                throw new LspProtocolException("Semantic token delta edit exceeds the cached token data.");
            }
            if (edit.Start + edit.DeleteCount > lastStart)
            {
                throw new LspProtocolException("Semantic token delta edits overlap.");
            }
            lastStart = edit.Start;
        }

        var result = previous.ToList();
        foreach (var edit in normalized)
        {
            result.RemoveRange(edit.Start, edit.DeleteCount);
            if (edit.Data.Count > 0)
            {
                result.InsertRange(edit.Start, edit.Data);
            }
        }
        return result;
    }

    private RocketSemanticTokensResult ParseDeltaOrFull(JsonElement response, IReadOnlyList<int> previous)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            throw new LspProtocolException("Semantic token delta response is not an object.");
        }
        if (response.TryGetProperty("data", out _))
        {
            return ParseFull(response);
        }
        if (!response.TryGetProperty("edits", out var editsElement) || editsElement.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException("Semantic token delta response is missing edits.");
        }

        var edits = editsElement.EnumerateArray().Select(ParseEdit).ToArray();
        var data = ApplyDelta(previous, edits);
        var resultId = ReadResultId(response);
        return new RocketSemanticTokensResult(resultId, data, Decode(data, _legend));
    }

    private RocketSemanticTokensResult ParseFull(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind != JsonValueKind.Array)
        {
            throw new LspProtocolException("Semantic token full response is missing data.");
        }
        var data = dataElement.EnumerateArray().Select(ReadTokenInteger).ToArray();
        return new RocketSemanticTokensResult(ReadResultId(response), data, Decode(data, _legend));
    }

    private static SemanticTokensEdit ParseEdit(JsonElement edit)
    {
        if (edit.ValueKind != JsonValueKind.Object ||
            !edit.TryGetProperty("start", out var startElement) || !startElement.TryGetInt32(out var start) ||
            !edit.TryGetProperty("deleteCount", out var deleteElement) || !deleteElement.TryGetInt32(out var deleteCount))
        {
            throw new LspProtocolException("Semantic token delta edit is malformed.");
        }
        var data = edit.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array
            ? dataElement.EnumerateArray().Select(ReadTokenInteger).ToArray()
            : Array.Empty<int>();
        return new SemanticTokensEdit(start, deleteCount, data);
    }

    private static int ReadTokenInteger(JsonElement value)
    {
        if (!value.TryGetInt32(out var number) || number < 0)
        {
            throw new LspProtocolException("Semantic token data contains an invalid integer.");
        }
        return number;
    }

    private static string? ReadResultId(JsonElement response) =>
        response.TryGetProperty("resultId", out var resultId) && resultId.ValueKind == JsonValueKind.String
            ? resultId.GetString()
            : null;

    private void Store(string uri, RocketSemanticTokensResult result) =>
        _cache[uri] = new CacheEntry(result.ResultId, result.EncodedData.ToArray());

    private sealed record CacheEntry(string? ResultId, IReadOnlyList<int> Data);
}
