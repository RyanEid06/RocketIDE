namespace RocketIDE.Rocket.Tools;

public interface IRocketToolLocator
{
    Task<RocketToolchain?> LocateAsync(string? activePath, CancellationToken cancellationToken);
    Task<RocketToolDiscoveryResult> DiscoverAsync(string? activePath, CancellationToken cancellationToken);
}
