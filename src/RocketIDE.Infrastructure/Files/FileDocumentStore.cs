using System.Security.Cryptography;
using System.Text;
using RocketIDE.Core.Documents;

namespace RocketIDE.Infrastructure.Files;

public sealed class FileDocumentStore : IDocumentStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DocumentId, Entry> _byId = new();
    private readonly List<DocumentId> _order = new();
    private readonly Func<string, byte[], CancellationToken, Task> _writeAtomicallyAsync;

    public FileDocumentStore()
        : this(WriteAtomicallyAsync)
    {
    }

    internal FileDocumentStore(Func<string, byte[], CancellationToken, Task> writeAtomicallyAsync)
    {
        _writeAtomicallyAsync = writeAtomicallyAsync ?? throw new ArgumentNullException(nameof(writeAtomicallyAsync));
    }

    public IReadOnlyList<DocumentSnapshot> OpenDocuments
    {
        get
        {
            lock (_gate)
            {
                return _order.Select(id => _byId[id].State.Snapshot).ToArray();
            }
        }
    }

    public async Task<DocumentSnapshot> OpenAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = NormalizePath(path);

        lock (_gate)
        {
            if (_byPath.TryGetValue(normalizedPath, out var existing))
            {
                return existing.State.Snapshot;
            }
        }

        var loaded = await ReadTextFileAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        var state = new DocumentState(DocumentId.New(), normalizedPath, loaded.Text, StrictUtf8.GetByteCount(loaded.Text));
        var entry = new Entry(state, loaded.Hash, loaded.HasUtf8Bom);

        lock (_gate)
        {
            if (_byPath.TryGetValue(normalizedPath, out var racedExisting))
            {
                return racedExisting.State.Snapshot;
            }

            _byPath.Add(normalizedPath, entry);
            _byId.Add(state.Id, entry);
            _order.Add(state.Id);
            return state.Snapshot;
        }
    }

    public DocumentSnapshot UpdateText(DocumentId id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (_gate)
        {
            return GetEntry(id).State.ApplyEdit(text);
        }
    }

    public async Task<DocumentSnapshot> ReloadAsync(DocumentId id, CancellationToken cancellationToken)
    {
        Entry entry;
        string path;
        lock (_gate)
        {
            entry = GetEntry(id);
            if (entry.State.Snapshot.IsDirty)
            {
                throw new InvalidOperationException("A document with unsaved editor changes cannot be reloaded from disk.");
            }

            path = entry.State.Path;
        }

        var loaded = await ReadTextFileAsync(path, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var current = GetEntry(id);
            if (current.State.Snapshot.IsDirty)
            {
                throw new InvalidOperationException("A document became dirty while it was being reloaded from disk.");
            }

            current.BaselineHash = loaded.Hash;
            current.HasUtf8Bom = loaded.HasUtf8Bom;
            return current.State.ReplaceFromDisk(loaded.Text, StrictUtf8.GetByteCount(loaded.Text));
        }
    }

    public async Task<DocumentSaveResult> SaveAsync(
        DocumentId id,
        bool overwriteExternalChanges,
        CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_gate)
        {
            entry = GetEntry(id);
        }

        await entry.SaveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DocumentSnapshot snapshot;
            byte[] baselineHash;
            bool hasUtf8Bom;
            lock (_gate)
            {
                // Re-read state only after this document's previous save has finished. Two rapid
                // Ctrl+S operations must not race atomic replacements and leave BaselineHash
                // describing a different write than the bytes currently on disk.
                var current = GetEntry(id);
                snapshot = current.State.Snapshot;
                baselineHash = current.BaselineHash.ToArray();
                hasUtf8Bom = current.HasUtf8Bom;
            }

            if (!snapshot.IsDirty)
            {
                return new DocumentSaveResult(DocumentSaveStatus.NoChanges, snapshot);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!overwriteExternalChanges)
            {
                var currentHash = await TryGetCurrentHashAsync(snapshot.Path, cancellationToken).ConfigureAwait(false);
                if (currentHash is null || !CryptographicOperations.FixedTimeEquals(currentHash, baselineHash))
                {
                    return new DocumentSaveResult(
                        DocumentSaveStatus.Conflict,
                        snapshot,
                        "The file changed on disk after it was opened or last saved.");
                }
            }

            var bytes = Encode(snapshot.Path, snapshot.Text, hasUtf8Bom);
            await _writeAtomicallyAsync(snapshot.Path, bytes, cancellationToken).ConfigureAwait(false);
            var newHash = SHA256.HashData(bytes);

            lock (_gate)
            {
                // The editor may change while the async write is in flight. The disk baseline must
                // always become the exact snapshot that was persisted, independently of current text.
                var current = GetEntry(id);
                current.BaselineHash = newHash;
                var persisted = current.State.MarkPersisted(snapshot.Text, StrictUtf8.GetByteCount(snapshot.Text));
                return new DocumentSaveResult(DocumentSaveStatus.Saved, persisted);
            }
        }
        finally
        {
            entry.SaveGate.Release();
        }
    }

    public async Task<IReadOnlyList<DocumentSaveResult>> SaveAllAsync(CancellationToken cancellationToken)
    {
        DocumentId[] ids;
        lock (_gate)
        {
            ids = _order.ToArray();
        }

        var results = new List<DocumentSaveResult>(ids.Length);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await SaveAsync(id, overwriteExternalChanges: false, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public bool TryGet(DocumentId id, out DocumentSnapshot? document)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var entry))
            {
                document = entry.State.Snapshot;
                return true;
            }

            document = null;
            return false;
        }
    }

    public bool Close(DocumentId id)
    {
        lock (_gate)
        {
            if (!_byId.Remove(id, out var entry))
            {
                return false;
            }

            _byPath.Remove(entry.State.Path);
            _order.Remove(id);
            return true;
        }
    }

    private Entry GetEntry(DocumentId id)
    {
        if (!_byId.TryGetValue(id, out var entry))
        {
            throw new KeyNotFoundException($"Document '{id}' is not open.");
        }

        return entry;
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static async Task<LoadedTextFile> ReadTextFileAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            throw new UnsupportedTextFileException(path, "the file contains NUL bytes and appears to be binary");
        }

        var preamble = Utf8WithBom.GetPreamble();
        var hasBom = bytes.AsSpan().StartsWith(preamble);
        var offset = hasBom ? preamble.Length : 0;

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new UnsupportedTextFileException(path, "the file is not valid UTF-8", exception);
        }

        return new LoadedTextFile(text, bytes, SHA256.HashData(bytes), hasBom);
    }

    private static async Task<byte[]?> TryGetCurrentHashAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return SHA256.HashData(bytes);
    }

    private static byte[] Encode(string path, string text, bool includeBom)
    {
        var encoding = includeBom ? Utf8WithBom : Utf8NoBom;
        byte[] content;
        try
        {
            content = encoding.GetBytes(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new UnsupportedTextFileException(path, "the editor buffer contains invalid Unicode data", exception);
        }
        if (!includeBom)
        {
            return content;
        }

        var preamble = encoding.GetPreamble();
        var bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException($"Cannot determine the parent directory for '{path}'.");
        }

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.rocketide-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed class Entry(DocumentState state, byte[] baselineHash, bool hasUtf8Bom)
    {
        public DocumentState State { get; } = state;

        public byte[] BaselineHash { get; set; } = baselineHash;

        public bool HasUtf8Bom { get; set; } = hasUtf8Bom;

        public SemaphoreSlim SaveGate { get; } = new(1, 1);
    }

    private sealed record LoadedTextFile(string Text, byte[] Bytes, byte[] Hash, bool HasUtf8Bom);
}
