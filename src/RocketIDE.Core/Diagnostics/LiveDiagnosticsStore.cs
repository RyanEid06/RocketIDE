namespace RocketIDE.Core.Diagnostics;

public enum LiveDiagnosticDocumentState
{
    Offline,
    Awaiting,
    Current,
    Stale,
    Unsupported,
}

public sealed record LiveDiagnosticDocumentSnapshot(
    string DocumentUri,
    string FilePath,
    int? DocumentVersion,
    int? PublicationVersion,
    LiveDiagnosticDocumentState State,
    IReadOnlyList<RocketDiagnostic> Diagnostics,
    bool IsOpen);

public sealed class LiveDiagnosticsStore
{
    private readonly Dictionary<string, Entry> _entries = new(UriComparer);
    private readonly HashSet<string> _closedDocumentUris = new(UriComparer);

    public long Generation { get; private set; }

    public bool IsOnline { get; private set; }

    public IReadOnlyCollection<LiveDiagnosticDocumentSnapshot> Snapshots =>
        _entries.Values.Select(ToSnapshot).ToArray();

    public void BeginSession(long generation, bool isOnline)
    {
        Generation = generation;
        IsOnline = isOnline;
        _closedDocumentUris.Clear();

        foreach (var key in _entries.Where(pair => !pair.Value.IsOpen).Select(pair => pair.Key).ToArray())
        {
            _entries.Remove(key);
        }

        foreach (var entry in _entries.Values)
        {
            entry.PublicationVersion = null;
            entry.HasPublication = false;
            entry.Diagnostics = [];
            entry.State = !entry.IsSupported
                ? LiveDiagnosticDocumentState.Unsupported
                : isOnline
                    ? LiveDiagnosticDocumentState.Awaiting
                    : LiveDiagnosticDocumentState.Offline;
        }
    }

    public void TrackDocument(string path, int version, bool isSupported)
    {
        var normalizedPath = NormalizePath(path);
        var documentUri = ToDocumentUri(normalizedPath);
        _closedDocumentUris.Remove(documentUri);
        if (!_entries.TryGetValue(documentUri, out var entry))
        {
            entry = new Entry(documentUri, normalizedPath);
            _entries.Add(documentUri, entry);
        }

        var previousVersion = entry.DocumentVersion;
        var previousPublicationVersion = entry.PublicationVersion;
        var hadCurrentPublication = entry.State == LiveDiagnosticDocumentState.Current;
        var hadPublication = entry.HasPublication;

        entry.IsOpen = true;
        entry.IsSupported = isSupported;
        entry.DocumentVersion = version;

        if (!isSupported)
        {
            entry.State = LiveDiagnosticDocumentState.Unsupported;
            entry.PublicationVersion = null;
            entry.HasPublication = false;
            entry.Diagnostics = [];
            return;
        }

        if (!IsOnline)
        {
            entry.State = LiveDiagnosticDocumentState.Offline;
            entry.PublicationVersion = null;
            entry.HasPublication = false;
            entry.Diagnostics = [];
            return;
        }

        if (hadCurrentPublication &&
            (previousPublicationVersion is null || previousPublicationVersion == version) &&
            (previousVersion is null || previousVersion == version))
        {
            return;
        }

        entry.State = hadPublication
            ? LiveDiagnosticDocumentState.Stale
            : LiveDiagnosticDocumentState.Awaiting;
        entry.Diagnostics = [];
    }

    public void UpdateDocument(string path, int version, bool isSupported) =>
        TrackDocument(path, version, isSupported);

    public void UntrackDocument(string path)
    {
        var documentUri = ToDocumentUri(NormalizePath(path));
        _entries.Remove(documentUri);
        _closedDocumentUris.Add(documentUri);
    }

    public bool Publish(
        long generation,
        string documentUri,
        string path,
        int? version,
        IReadOnlyList<RocketDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!IsOnline || generation != Generation)
        {
            return false;
        }

        var normalizedUri = NormalizeDocumentUri(documentUri);
        var normalizedPath = NormalizePath(path);
        if (_closedDocumentUris.Contains(normalizedUri))
        {
            // Closing the editor is authoritative for this session. A non-empty publication
            // already queued by the server must not resurrect Problems/squiggles before the
            // server's empty didClose publication arrives. Reopening the document or starting
            // a new LSP generation clears this tombstone.
            return diagnostics.Count == 0;
        }
        if (!_entries.TryGetValue(normalizedUri, out var entry))
        {
            // Empty diagnostics for an unopened document carry no state worth retaining. In
            // particular, didClose may race after the editor has already removed its entry.
            if (diagnostics.Count == 0)
            {
                return true;
            }

            entry = new Entry(normalizedUri, normalizedPath)
            {
                IsOpen = false,
                IsSupported = true,
                State = LiveDiagnosticDocumentState.Awaiting,
            };
            _entries.Add(normalizedUri, entry);
        }

        if (!entry.IsSupported)
        {
            return false;
        }

        if (entry.IsOpen &&
            entry.DocumentVersion is { } currentVersion &&
            version is { } publicationVersion &&
            publicationVersion != currentVersion)
        {
            // Out-of-order publications must not erase a newer current result. If the editor
            // has already advanced without a matching publication, TrackDocument has already
            // placed the entry in Stale; otherwise preserve the current snapshot as-is.
            return false;
        }

        if (!entry.IsOpen &&
            entry.PublicationVersion is { } previousPublicationVersion &&
            version is { } incomingPublicationVersion &&
            incomingPublicationVersion < previousPublicationVersion)
        {
            return false;
        }

        if (!entry.IsOpen && diagnostics.Count == 0)
        {
            _entries.Remove(normalizedUri);
            return true;
        }

        entry.PublicationVersion = version;
        entry.HasPublication = true;
        entry.Diagnostics = diagnostics.ToArray();
        entry.State = LiveDiagnosticDocumentState.Current;
        return true;
    }

    public LiveDiagnosticDocumentSnapshot? GetSnapshot(string path)
    {
        var key = ToDocumentUri(NormalizePath(path));
        return _entries.TryGetValue(key, out var entry) ? ToSnapshot(entry) : null;
    }

    private static LiveDiagnosticDocumentSnapshot ToSnapshot(Entry entry) =>
        new(
            entry.DocumentUri,
            entry.FilePath,
            entry.DocumentVersion,
            entry.PublicationVersion,
            entry.State,
            entry.Diagnostics,
            entry.IsOpen);

    private static StringComparer UriComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static string ToDocumentUri(string normalizedPath) => new Uri(normalizedPath).AbsoluteUri;

    private static string NormalizeDocumentUri(string documentUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentUri);
        if (!Uri.TryCreate(documentUri, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            throw new ArgumentException("Diagnostic document URI must be an absolute file URI.", nameof(documentUri));
        }

        return uri.AbsoluteUri;
    }

    private sealed class Entry(string documentUri, string filePath)
    {
        public string DocumentUri { get; } = documentUri;
        public string FilePath { get; } = filePath;
        public int? DocumentVersion { get; set; }
        public int? PublicationVersion { get; set; }
        public bool HasPublication { get; set; }
        public LiveDiagnosticDocumentState State { get; set; } = LiveDiagnosticDocumentState.Offline;
        public IReadOnlyList<RocketDiagnostic> Diagnostics { get; set; } = [];
        public bool IsOpen { get; set; }
        public bool IsSupported { get; set; } = true;
    }
}
