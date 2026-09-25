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
    event EventHandler? SessionChanged;
    Task<RocketFoldingSnapshot?> GetRangesAsync(DocumentTabViewModel document, CancellationToken cancellationToken);
}

public sealed class RocketFoldingRangeProvider : IRocketFoldingRangeProvider
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<RocketFoldingRange>?>> _request;
    private readonly Func<long> _sessionGeneration;
    private readonly CancellationToken _lifetimeToken;
    public RocketFoldingRangeProvider(
        Func<string, CancellationToken, Task<IReadOnlyList<RocketFoldingRange>?>> request,
        Func<long> sessionGeneration,
        CancellationToken lifetimeToken = default)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _sessionGeneration = sessionGeneration ?? throw new ArgumentNullException(nameof(sessionGeneration));
        _lifetimeToken = lifetimeToken;
    }

    public event EventHandler? SessionChanged;

    public void NotifySessionChanged() => SessionChanged?.Invoke(this, EventArgs.Empty);

    public async Task<RocketFoldingSnapshot?> GetRangesAsync(DocumentTabViewModel document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = Path.GetFullPath(document.Path);
        var version = document.Version;
        var generation = _sessionGeneration();
        if (_lifetimeToken.IsCancellationRequested || generation <= 0) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        var ranges = await _request(path, linked.Token).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (ranges is null || document.Version != version || _sessionGeneration() != generation || _lifetimeToken.IsCancellationRequested)
        {
            return null;
        }
        return new RocketFoldingSnapshot(path, version, generation, ranges);
    }

}
