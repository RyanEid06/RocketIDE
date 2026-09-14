using System.Text.Json;
using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class RocketDebugSourceMapTests
{
    [TestMethod]
    public void ReadResolvesSourcesByExactRelativePathAndBasename()
    {
        using var temp = new TempDirectory();
        var main = temp.CreateFile("src/main.rocket", "fn main():\n    return 0\n");
        var util = temp.CreateFile("src/lib/util.rocket", "fn util():\n    return 1\n");
        var map = temp.CreateFile("out/app.rocket.map.json", JsonSerializer.Serialize(new
        {
            format = "rocket-source-map-1",
            functions = new object[]
            {
                new { source = "src/main.rocket", symbol = "main", locations = new[] { new { source = "src/lib/util.rocket" } } },
            },
        }));

        var sourceMap = RocketDebugSourceMap.Read(map, temp.Path);

        Assert.AreEqual(Path.GetFullPath(main), sourceMap.ResolveDebuggerSource("main.rocket"));
        Assert.AreEqual(Path.GetFullPath(util), sourceMap.ResolveDebuggerSource("rocket:\\source\\util.rocket"));
        Assert.AreEqual(2, sourceMap.Sources.Count);
    }

    [TestMethod]
    public void ReadRejectsDuplicateBasenames()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a/main.rocket", "a");
        temp.CreateFile("b/main.rocket", "b");
        var map = temp.CreateFile("app.rocket.map.json", "{\"format\":\"rocket-source-map-1\",\"functions\":[{\"source\":\"a/main.rocket\"},{\"source\":\"b/main.rocket\"}]}");

        Assert.ThrowsExactly<InvalidDataException>(() => RocketDebugSourceMap.Read(map, temp.Path));
    }

    [TestMethod]
    public void ReadRejectsMissingSourceAndWrongSchema()
    {
        using var temp = new TempDirectory();
        var missing = temp.CreateFile("missing.rocket.map.json", "{\"format\":\"rocket-source-map-1\",\"functions\":[{\"source\":\"gone.rocket\"}]}");
        var wrong = temp.CreateFile("wrong.rocket.map.json", "{\"format\":\"other\",\"functions\":[]}");

        Assert.ThrowsExactly<FileNotFoundException>(() => RocketDebugSourceMap.Read(missing, temp.Path));
        Assert.ThrowsExactly<InvalidDataException>(() => RocketDebugSourceMap.Read(wrong, temp.Path));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-debug-map-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string relative, string text)
        {
            var path = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
