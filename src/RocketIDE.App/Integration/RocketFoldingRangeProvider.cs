using System.IO;
using RocketIDE.App.ViewModels;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Integration;

public sealed record RocketFoldingSnapshot(
    string Path,
    int Version,
    long SessionGeneration,
    IReadOnlyList<RocketFoldingRange> Ranges);

public interface IRocketFoldingRangeProvider
{
    Task<RocketFoldingSnapshot?> GetRangesAsync(DocumentTabViewModel document, CancellationToken cancellationToken);
}

public sealed class RocketFoldingRangeProvider : IRocketFoldingRangeProvider
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<RocketFoldingRange>?>> _request;
    private readonly Func<long> _sessionGeneration;
    private readonly Dictionary<string, long> _latestRequest = new(PathComparer);
    private readonly object _sync = new();
    private long _sequence;

    public RocketFoldingRangeProvider(
        Func<string, CancellationToken, Task<IReadOnlyList<RocketFoldingRange>?>> request,
        Func<long> sessionGeneration)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _sessionGeneration = sessionGeneration ?? throw new ArgumentNullException(nameof(sessionGeneration));
    }

    public async Task<RocketFoldingSnapshot?> GetRangesAsync(DocumentTabViewModel document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = Path.GetFullPath(document.Path);
        var version = document.Version;
        var generation = _sessionGeneration();
        var requestId = Interlocked.Increment(ref _sequence);
        lock (_sync) _latestRequest[path] = requestId;

        var ranges = await _request(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_latestRequest.TryGetValue(path, out var latest) || latest != requestId)
            {
                return null;
            }
        }
        if (ranges is null || document.Version != version || _sessionGeneration() != generation || generation <= 0)
        {
            return null;
        }
        return new RocketFoldingSnapshot(path, version, generation, ranges);
    }

    private static IEqualityComparer<string> PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
