using System.Text.Json;
using RocketIDE.Rocket.Debugger;

namespace RocketIDE.Rocket.Tests.Debugger;

[TestClass]
public sealed class RocketDebugArtifactValidatorTests
{
    [TestMethod]
    public void ValidatorAcceptsMatchingArtifactsAndSourceMap()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateFile("src/main.rocket", "fn main():\n    return 0\n");
        var exe = temp.CreateFile(".rocketc/main.exe", "exe");
        var pdb = temp.CreateFile(".rocketc/main.pdb", "pdb");
        var map = temp.CreateFile(".rocketc/main.rocket.map.json", JsonSerializer.Serialize(new
        {
            format = "rocket-source-map-1",
            functions = new[] { new { source = Path.GetRelativePath(temp.Path, source), locations = Array.Empty<object>() } },
        }));

        var result = RocketDebugArtifactValidator.Validate(exe, pdb, map, temp.Path);

        Assert.IsTrue(result.IsValid, result.Summary);
        CollectionAssert.Contains(result.Sources.ToArray(), Path.GetRelativePath(temp.Path, source));
    }

    [TestMethod]
    public void ValidatorReportsDuplicateSourceBasenames()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a/main.rocket", "a");
        temp.CreateFile("b/main.rocket", "b");
        var exe = temp.CreateFile("main.exe", "exe");
        var pdb = temp.CreateFile("main.pdb", "pdb");
        var map = temp.CreateFile("main.rocket.map.json", "{\"format\":\"rocket-source-map-1\",\"functions\":[{\"source\":\"main.rocket\"}]}");

        var result = RocketDebugArtifactValidator.Validate(exe, pdb, map, temp.Path);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Summary, "ambiguous");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-debugger-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string relativePath, string contents)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            var directory = System.IO.Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
