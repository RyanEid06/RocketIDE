using RocketIDE.Rocket.Tools;

namespace RocketIDE.Rocket.Tests.Tools;

[TestClass]
public sealed class RocketToolLocatorTests
{
    [TestMethod]
    public async Task LocateAsync_UsesExplicitPathsBeforeEnvironmentAndPath()
    {
        using var temp = new TempDirectory();
        var explicitCompiler = temp.CreateFile("explicit/rocketc.exe");
        var explicitLsp = temp.CreateFile("explicit/rocket-lsp.exe");
        var envCompiler = temp.CreateFile("env/rocketc.exe");
        var envLsp = temp.CreateFile("env/rocket-lsp.exe");
        var probe = new FakeProbe();
        var locator = new RocketToolLocator(
            new RocketToolDiscoveryOptions(explicitCompiler, explicitLsp, temp.Path),
            probe,
            name => name switch
            {
                "ROCKET_COMPILER" => envCompiler,
                "ROCKET_LANGUAGE_SERVER" => envLsp,
                _ => null,
            });

        var result = await locator.LocateAsync(null, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(Path.GetFullPath(explicitCompiler), result.CompilerPath);
        Assert.AreEqual(Path.GetFullPath(explicitLsp), result.LanguageServerPath);
        Assert.AreEqual("rocketc test-version", result.CompilerVersion);
        Assert.AreEqual("rocket-lsp test-version", result.LanguageServerVersion);
    }

    [TestMethod]
    public async Task DiscoverAsync_EnvironmentLanguageServerOverrideWinsBeforeCompilerSibling()
    {
        using var temp = new TempDirectory();
        var compiler = temp.CreateFile("checkout/out/build/windows-debug/rocketc.exe");
        var siblingLsp = temp.CreateFile("checkout/out/build/windows-debug/rocket-lsp.exe");
        var envLsp = temp.CreateFile("env/rocket-lsp.exe");
        var activeFile = temp.CreateFile("checkout/src/main.rocket");
        var locator = new RocketToolLocator(
            new RocketToolDiscoveryOptions(null, null, temp.Path),
            new FakeProbe(),
            name => name switch
            {
                "ROCKET_COMPILER" => compiler,
                "ROCKET_LANGUAGE_SERVER" => envLsp,
                _ => null,
            });

        var result = await locator.DiscoverAsync(activeFile, CancellationToken.None);

        // Explicit/environment LSP has higher priority than sibling by contract.
        Assert.AreEqual(Path.GetFullPath(envLsp), result.LanguageServerPath);
        Assert.AreNotEqual(Path.GetFullPath(siblingLsp), result.LanguageServerPath);
    }

    [TestMethod]
    public async Task DiscoverAsync_UsesCompilerSiblingBeforeCheckoutAndPathWhenNoLspOverrideExists()
    {
        using var temp = new TempDirectory();
        var compiler = temp.CreateFile("selected/rocketc.exe");
        var siblingLsp = temp.CreateFile("selected/rocket-lsp.exe");
        var checkoutLsp = temp.CreateFile("checkout/out/build/windows-debug/rocket-lsp.exe");
        var activeFile = temp.CreateFile("checkout/src/main.rocket");
        var locator = new RocketToolLocator(
            new RocketToolDiscoveryOptions(compiler, null, temp.Path),
            new FakeProbe(),
            _ => null);

        var result = await locator.DiscoverAsync(activeFile, CancellationToken.None);

        Assert.AreEqual(Path.GetFullPath(siblingLsp), result.LanguageServerPath);
        Assert.AreNotEqual(Path.GetFullPath(checkoutLsp), result.LanguageServerPath);
    }

    [TestMethod]
    public async Task DiscoverAsync_FindsRecognizedActiveCheckoutOutputWithoutHardCodedRepositoryPath()
    {
        using var temp = new TempDirectory();
        var compiler = temp.CreateFile("arbitrary-clone/out/build/windows-release/rocketc.exe");
        var lsp = temp.CreateFile("arbitrary-clone/out/build/windows-release/rocket-lsp.exe");
        var activeFile = temp.CreateFile("arbitrary-clone/examples/demo.rocket");
        var locator = new RocketToolLocator(new RocketToolDiscoveryOptions(null, null, temp.Path), new FakeProbe(), _ => null);

        var result = await locator.DiscoverAsync(activeFile, CancellationToken.None);

        Assert.AreEqual(Path.GetFullPath(compiler), result.CompilerPath);
        Assert.AreEqual(Path.GetFullPath(lsp), result.LanguageServerPath);
    }


    [TestMethod]
    public async Task DiscoverAsync_UsesEnvironmentOverridesWhenExplicitSettingsAreEmpty()
    {
        using var temp = new TempDirectory();
        var compiler = temp.CreateFile("env/rocketc.exe");
        var lsp = temp.CreateFile("env/rocket-lsp.exe");
        var locator = new RocketToolLocator(
            new RocketToolDiscoveryOptions(null, null, temp.Path),
            new FakeProbe(),
            name => name switch
            {
                "ROCKET_COMPILER" => compiler,
                "ROCKET_LANGUAGE_SERVER" => lsp,
                _ => null,
            });

        var result = await locator.DiscoverAsync(null, CancellationToken.None);

        Assert.AreEqual(Path.GetFullPath(compiler), result.CompilerPath);
        Assert.AreEqual(Path.GetFullPath(lsp), result.LanguageServerPath);
    }

    [TestMethod]
    public async Task DiscoverAsync_UsesPathWhenHigherPrioritySourcesAreMissing()
    {
        using var temp = new TempDirectory();
        var pathDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "path-tools")).FullName;
        var compiler = temp.CreateFile("path-tools/rocketc.exe");
        var lsp = temp.CreateFile("path-tools/rocket-lsp.exe");
        var locator = new RocketToolLocator(
            new RocketToolDiscoveryOptions(null, null, temp.Path),
            new FakeProbe(),
            name => name == "PATH" ? pathDirectory : null);

        var result = await locator.DiscoverAsync(null, CancellationToken.None);

        Assert.AreEqual(Path.GetFullPath(compiler), result.CompilerPath);
        Assert.AreEqual(Path.GetFullPath(lsp), result.LanguageServerPath);
    }

    [TestMethod]
    public async Task DiscoverAsync_UsesBundledSdkCandidateAsFinalFallback()
    {
        using var temp = new TempDirectory();
        var compiler = temp.CreateFile("sdk/bin/rocketc.exe");
        var lsp = temp.CreateFile("sdk/bin/rocket-lsp.exe");
        var locator = new RocketToolLocator(new RocketToolDiscoveryOptions(null, null, temp.Path), new FakeProbe(), _ => null);

        var result = await locator.DiscoverAsync(null, CancellationToken.None);

        Assert.AreEqual(Path.GetFullPath(compiler), result.CompilerPath);
        Assert.AreEqual(Path.GetFullPath(lsp), result.LanguageServerPath);
    }

    private sealed class FakeProbe : IRocketToolVersionProbe
    {
        public Task<string> GetVersionAsync(string executablePath, CancellationToken cancellationToken)
        {
            var name = Path.GetFileNameWithoutExtension(executablePath);
            return Task.FromResult($"{name} test-version");
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-tools-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string relativePath)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, string.Empty);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
