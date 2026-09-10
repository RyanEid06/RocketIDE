using System.Text;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer;

public enum LspDocumentSyncState
{
    NotOpen,
    Synchronized,
    LargeFileUnsupportedByLsp,
}

public sealed class DocumentSynchronizer(IRocketLanguageClient client)
{
    public const int MaxDocumentBytes = 4 * 1024 * 1024;

    private readonly Dictionary<string, DocumentEntry> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<LspDocumentSyncState> OpenAsync(string path, string text, int version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = NormalizePath(path);
            if (_documents.TryGetValue(key, out var existing))
            {
                if (existing.Version == version && string.Equals(existing.Text, text, StringComparison.Ordinal))
                {
                    return existing.State;
                }

                throw new InvalidOperationException($"Document '{key}' is already tracked by the Rocket LSP synchronizer with a different version.");
            }

            if (IsOversized(text))
            {
                _documents[key] = new DocumentEntry(ToUri(key), text, version, LspDocumentSyncState.LargeFileUnsupportedByLsp);
                return LspDocumentSyncState.LargeFileUnsupportedByLsp;
            }

            var uri = ToUri(key);
            await client.NotifyAsync(
                "textDocument/didOpen",
                new DidOpenTextDocumentParams(new TextDocumentItem(uri, "rocket", version, text)),
                cancellationToken).ConfigureAwait(false);
            _documents[key] = new DocumentEntry(uri, text, version, LspDocumentSyncState.Synchronized);
            return LspDocumentSyncState.Synchronized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LspDocumentSyncState> ChangeAsync(string path, string text, int version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = NormalizePath(path);
            if (!_documents.TryGetValue(key, out var current))
            {
                return LspDocumentSyncState.NotOpen;
            }
            if (version <= current.Version)
            {
                return current.State;
            }

            if (IsOversized(text))
            {
                if (current.State == LspDocumentSyncState.Synchronized)
                {
                    await client.NotifyAsync(
                        "textDocument/didClose",
                        new DidCloseTextDocumentParams(new TextDocumentIdentifier(current.Uri)),
                        cancellationToken).ConfigureAwait(false);
                }
                _documents[key] = current with { Text = text, Version = version, State = LspDocumentSyncState.LargeFileUnsupportedByLsp };
                return LspDocumentSyncState.LargeFileUnsupportedByLsp;
            }

            if (current.State == LspDocumentSyncState.LargeFileUnsupportedByLsp)
            {
                await client.NotifyAsync(
                    "textDocument/didOpen",
                    new DidOpenTextDocumentParams(new TextDocumentItem(current.Uri, "rocket", version, text)),
                    cancellationToken).ConfigureAwait(false);
                _documents[key] = current with { Text = text, Version = version, State = LspDocumentSyncState.Synchronized };
                return LspDocumentSyncState.Synchronized;
            }

            if (!string.Equals(current.Text, text, StringComparison.Ordinal))
            {
                var change = ComputeIncrementalChange(current.Text, text);
                await client.NotifyAsync(
                    "textDocument/didChange",
                    new DidChangeTextDocumentParams(
                        new VersionedTextDocumentIdentifier(current.Uri, version),
                        new[] { change }),
                    cancellationToken).ConfigureAwait(false);
            }

            _documents[key] = current with { Text = text, Version = version, State = LspDocumentSyncState.Synchronized };
            return LspDocumentSyncState.Synchronized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = NormalizePath(path);
            if (_documents.TryGetValue(key, out var current) && current.State == LspDocumentSyncState.Synchronized)
            {
                await client.NotifyAsync(
                    "textDocument/didSave",
                    new DidSaveTextDocumentParams(new TextDocumentIdentifier(current.Uri), current.Text),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = NormalizePath(path);
            if (!_documents.Remove(key, out var current) || current.State != LspDocumentSyncState.Synchronized)
            {
                return;
            }

            await client.NotifyAsync(
                "textDocument/didClose",
                new DidCloseTextDocumentParams(new TextDocumentIdentifier(current.Uri)),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static LspTextDocumentContentChangeEvent ComputeIncrementalChange(string oldText, string newText)
    {
        var prefix = 0;
        var maxPrefix = Math.Min(oldText.Length, newText.Length);
        while (prefix < maxPrefix && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }
        prefix = MoveBeforeSplitSurrogate(oldText, prefix);
        prefix = MoveBeforeSplitSurrogate(newText, prefix);

        var oldSuffix = oldText.Length;
        var newSuffix = newText.Length;
        while (oldSuffix > prefix && newSuffix > prefix && oldText[oldSuffix - 1] == newText[newSuffix - 1])
        {
            oldSuffix--;
            newSuffix--;
        }
        oldSuffix = MoveAfterSplitSurrogate(oldText, oldSuffix);
        newSuffix = MoveAfterSplitSurrogate(newText, newSuffix);

        var replacement = newText[prefix..newSuffix];
        return new LspTextDocumentContentChangeEvent(
            new LspRange(PositionAt(oldText, prefix), PositionAt(oldText, oldSuffix)),
            replacement);
    }

    private static int MoveBeforeSplitSurrogate(string text, int offset)
    {
        if (offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]))
        {
            return offset - 1;
        }
        return offset;
    }

    private static int MoveAfterSplitSurrogate(string text, int offset)
    {
        if (offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]))
        {
            return offset + 1;
        }
        return offset;
    }

    private static LspPosition PositionAt(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }
        return new LspPosition(line, offset - lineStart);
    }

    private static bool IsOversized(string text) => Encoding.UTF8.GetByteCount(text) > MaxDocumentBytes;
    private static string NormalizePath(string path) => Path.GetFullPath(path);
    private static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private sealed record DocumentEntry(string Uri, string Text, int Version, LspDocumentSyncState State);
}
