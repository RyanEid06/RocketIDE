namespace RocketIDE.Rocket.Tools;

public sealed class RocketEnvironmentValidator(IRocketToolLocator locator)
{
    public async Task<RocketEnvironmentValidationResult> ValidateAsync(string? activePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);
        var discovery = await locator.DiscoverAsync(activePath, cancellationToken).ConfigureAwait(false);
        return new RocketEnvironmentValidationResult(discovery.Toolchain, discovery.Problems);
    }
}
