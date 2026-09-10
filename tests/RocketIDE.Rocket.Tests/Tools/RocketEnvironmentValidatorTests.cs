using RocketIDE.Rocket.Tools;

namespace RocketIDE.Rocket.Tests.Tools;

[TestClass]
public sealed class RocketEnvironmentValidatorTests
{
    [TestMethod]
    public async Task ValidateAsync_ReportsMissingToolsWithoutInventingFallbackBehavior()
    {
        using var temp = new TempDirectory();
        var locator = new RocketToolLocator(new RocketToolDiscoveryOptions(null, null, temp.Path), new NoopProbe(), _ => null);
        var validator = new RocketEnvironmentValidator(locator);

        var result = await validator.ValidateAsync(null, CancellationToken.None);

        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.Toolchain);
        Assert.IsTrue(result.Problems.Any(problem => problem.Contains("rocketc.exe", StringComparison.Ordinal)));
        Assert.IsTrue(result.Problems.Any(problem => problem.Contains("rocket-lsp.exe", StringComparison.Ordinal)));
    }

    private sealed class NoopProbe : IRocketToolVersionProbe
    {
        public Task<string> GetVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("unused");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-validate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
