namespace RocketIDE.Rocket.Tools;

public sealed class RocketEnvironmentValidator(RocketToolLocator locator)
{
    public async Task<RocketEnvironmentValidationResult> ValidateAsync(string? activePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);
        var discovery = await locator.DiscoverAsync(activePath, cancellationToken).ConfigureAwait(false);
        return new RocketEnvironmentValidationResult(discovery.Toolchain, discovery.Problems);
    }
}
