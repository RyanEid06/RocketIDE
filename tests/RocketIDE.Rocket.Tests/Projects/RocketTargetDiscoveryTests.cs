using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Tests.Projects;

[TestClass]
public sealed class RocketTargetDiscoveryTests
{
    [TestMethod]
    public void Discover_UsesNearestAncestorManifestForNestedRocketFile()
    {
        using var temp = new TempDirectory();
        var project = Directory.CreateDirectory(Path.Combine(temp.Path, "game"));
        Directory.CreateDirectory(Path.Combine(project.FullName, "src", "nested"));
        File.WriteAllText(Path.Combine(project.FullName, "rocket.toml"), "[package]\nname = \"game\"\nentry = \"src/main.rocket\"\n");
        var active = Path.Combine(project.FullName, "src", "nested", "player.rocket");
        File.WriteAllText(active, "fn player() -> Int:\n    return 1\n");

        var target = new RocketTargetDiscovery().Discover(active);

        Assert.IsNotNull(target);
        Assert.AreEqual(Path.GetFullPath(active), target.InputPath);
        Assert.AreEqual(Path.GetFullPath(project.FullName), target.WorkingDirectory);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(project.FullName, "rocket.toml")), target.ManifestPath);
        Assert.IsFalse(target.IsStandalone);
    }

    [TestMethod]
    public void Discover_ManifestUsesConfiguredEntry()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "code"));
        var manifest = Path.Combine(temp.Path, "rocket.toml");
        File.WriteAllText(manifest, "[package]\nname = \"demo\"\nentry = \"code/start.rocket\"\n");

        var target = new RocketTargetDiscovery().Discover(manifest);

        Assert.IsNotNull(target);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(temp.Path, "code", "start.rocket")), target.InputPath);
        Assert.AreEqual(Path.GetFullPath(temp.Path), target.WorkingDirectory);
        Assert.AreEqual(Path.GetFullPath(manifest), target.ManifestPath);
        Assert.IsFalse(target.IsStandalone);
    }

    [TestMethod]
    public void Discover_RocketFileWithoutManifestIsStandalone()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "single.rocket");
        File.WriteAllText(file, "fn main() -> Int:\n    return 0\n");

        var target = new RocketTargetDiscovery().Discover(file);

        Assert.IsNotNull(target);
        Assert.AreEqual(Path.GetFullPath(file), target.InputPath);
        Assert.AreEqual(Path.GetFullPath(temp.Path), target.WorkingDirectory);
        Assert.IsNull(target.ManifestPath);
        Assert.IsTrue(target.IsStandalone);
    }

    [TestMethod]
    public void Discover_NonRocketAndMissingPathsReturnNull()
    {
        using var temp = new TempDirectory();
        var textFile = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(textFile, "notes");

        var discovery = new RocketTargetDiscovery();

        Assert.IsNull(discovery.Discover(textFile));
        Assert.IsNull(discovery.Discover(Path.Combine(temp.Path, "missing.rocket")));
        Assert.IsNull(discovery.Discover(string.Empty));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-target-{Guid.NewGuid():N}");
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
