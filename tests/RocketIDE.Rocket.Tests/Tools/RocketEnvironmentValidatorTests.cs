using RocketIDE.Rocket.Tools;

namespace RocketIDE.Rocket.Tests.Tools;

[TestClass]
public sealed class RocketEnvironmentValidatorTests
{
    [TestMethod]
    public async Task ValidateAsync_ReportsMissingToolsWithoutInventingFallbackBehavior()
    {
        var discovery = new RocketToolDiscoveryResult(
            null,
            null,
            null,
            null,
            ["rocketc.exe was not found.", "rocket-lsp.exe was not found."]);
        var validator = new RocketEnvironmentValidator(new FakeLocator(discovery));

        var result = await validator.ValidateAsync(null, CancellationToken.None);

        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.Toolchain);
        Assert.IsTrue(result.Problems.Any(problem => problem.Contains("rocketc.exe", StringComparison.Ordinal)));
        Assert.IsTrue(result.Problems.Any(problem => problem.Contains("rocket-lsp.exe", StringComparison.Ordinal)));
    }

    private sealed class FakeLocator(RocketToolDiscoveryResult discovery) : IRocketToolLocator
    {
        public Task<RocketToolchain?> LocateAsync(string? activePath, CancellationToken cancellationToken) =>
            Task.FromResult(discovery.Toolchain);

        public Task<RocketToolDiscoveryResult> DiscoverAsync(string? activePath, CancellationToken cancellationToken) =>
            Task.FromResult(discovery);
    }
}
