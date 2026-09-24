using System.IO;
using RocketIDE.App.ViewModels;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Integration;

public sealed record DocumentSymbolSnapshot(
    string Path,
    int Version,
    long SessionGeneration,
    IReadOnlyList<RocketDocumentSymbol> Symbols);

public sealed class DocumentSymbolService
{
    private const int MaxCachedDocuments = 64;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<RocketDocumentSymbol>?>> _request;
    private readonly Func<long> _sessionGeneration;
    private readonly Dictionary<string, DocumentSymbolSnapshot> _cache = new(PathComparer);
    private readonly LinkedList<string> _lru = new();
    private readonly object _sync = new();

    public DocumentSymbolService(
        Func<string, CancellationToken, Task<IReadOnlyList<RocketDocumentSymbol>?>> request,
        Func<long> sessionGeneration)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _sessionGeneration = sessionGeneration ?? throw new ArgumentNullException(nameof(sessionGeneration));
    }

    public async Task<DocumentSymbolSnapshot?> GetAsync(DocumentTabViewModel document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = Path.GetFullPath(document.Path);
        var version = document.Version;
        var generation = _sessionGeneration();
        if (generation <= 0 || !document.AllowLsp) return null;

        lock (_sync)
        {
            if (_cache.TryGetValue(path, out var cached) && cached.Version == version && cached.SessionGeneration == generation)
            {
                Touch(path);
                return cached;
            }
        }

        var symbols = await _request(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (symbols is null || document.Version != version || _sessionGeneration() != generation)
        {
            return null;
        }

        var snapshot = new DocumentSymbolSnapshot(path, version, generation, symbols);
        lock (_sync)
        {
            _cache[path] = snapshot;
            Touch(path);
            while (_cache.Count > MaxCachedDocuments && _lru.First is { } oldest)
            {
                _lru.RemoveFirst();
                _cache.Remove(oldest.Value);
            }
        }
        return snapshot;
    }

    public void Invalidate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_sync)
        {
            _cache.Remove(fullPath);
            var node = FindLruNode(fullPath);
            if (node is not null) _lru.Remove(node);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _cache.Clear();
            _lru.Clear();
        }
    }

    private void Touch(string path)
    {
        var node = FindLruNode(path);
        if (node is not null) _lru.Remove(node);
        _lru.AddLast(path);
    }

    private LinkedListNode<string>? FindLruNode(string path)
    {
        for (var node = _lru.First; node is not null; node = node.Next)
        {
            if (PathComparer.Equals(node.Value, path)) return node;
        }
        return null;
    }

    private static IEqualityComparer<string> PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
