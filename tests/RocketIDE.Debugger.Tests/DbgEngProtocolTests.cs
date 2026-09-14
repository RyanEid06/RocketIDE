using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class DbgEngProtocolTests
{
    [TestMethod]
    public void BreakpointCommandUsesSourceLineExpressionAndRejectsInjection()
    {
        Assert.AreEqual("bp `main.rocket:27`", DbgEngProtocol.BuildSourceBreakpointCommand("main.rocket", 27));
        Assert.ThrowsExactly<ArgumentException>(() => DbgEngProtocol.BuildSourceBreakpointCommand("bad;g.rocket", 1));
        Assert.ThrowsExactly<ArgumentException>(() => DbgEngProtocol.BuildSourceBreakpointCommand("bad`name.rocket", 1));
    }

    [TestMethod]
    public void ParseThreadsRecognizesCurrentThreadAndHexSystemId()
    {
        const string text = """
            .  0  Id: 1c44.2abc Suspend: 1 Teb: 00000000 Unfrozen
               1  Id: 1c44.3def Suspend: 1 Teb: 00000000 Unfrozen
            """;

        var threads = DbgEngProtocol.ParseThreads(text);

        Assert.AreEqual(2, threads.Count);
        Assert.IsTrue(threads[0].IsCurrent);
        Assert.AreEqual(0x2abc, threads[0].SystemId);
        Assert.AreEqual(1, threads[1].Index);
    }

    [TestMethod]
    public void ParseFramesResolvesRocketSourceThroughMap()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateFile("src/main.rocket", "fn main():\n    return 0\n");
        var mapPath = temp.CreateFile("app.rocket.map.json", "{\"format\":\"rocket-source-map-1\",\"functions\":[{\"source\":\"src/main.rocket\"}]}");
        var map = RocketDebugSourceMap.Read(mapPath, temp.Path);
        const string stack = "00 00000000 00000000 app!main+0x4 [rocket:\\source\\main.rocket @ 7]";

        var frames = DbgEngProtocol.ParseStackFrames(stack, map);

        Assert.AreEqual(1, frames.Count);
        Assert.AreEqual(Path.GetFullPath(source), frames[0].SourcePath);
        Assert.AreEqual(7, frames[0].Line);
        StringAssert.Contains(frames[0].FunctionName, "app!main");
    }

    [TestMethod]
    public void ParseLocalsKeepsUnknownLinesInsteadOfInventingSemantics()
    {
        const string locals = """
            int count = 0n3
            rocket_string name = {len=0n4 ptr=0x1234}
            unusual debugger output
            """;

        var values = DbgEngProtocol.ParseLocals(locals);

        Assert.AreEqual(3, values.Count);
        Assert.AreEqual("count", values[0].Name);
        Assert.AreEqual("int", values[0].Type);
        Assert.AreEqual("0n3", values[0].Value);
        Assert.AreEqual("unusual debugger output", values[2].Value);
    }

    [TestMethod]
    public void ParseCurrentLocationAcceptsUnbracketedDbgEngSourceLine()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateFile("main.rocket", "fn main():\n    return 0\n");
        var mapPath = temp.CreateFile("app.rocket.map.json", "{\"format\":\"rocket-source-map-1\",\"functions\":[{\"source\":\"main.rocket\"}]}");
        var map = RocketDebugSourceMap.Read(mapPath, temp.Path);

        var location = DbgEngProtocol.ParseCurrentLocation("rocket:\\source\\main.rocket @ 9", map, "breakpoint");

        Assert.IsNotNull(location);
        Assert.AreEqual(Path.GetFullPath(source), location.SourcePath);
        Assert.AreEqual(9, location.Line);
    }

    [TestMethod]
    public void ParseCurrentLocationAcceptsLogicalRocketPath()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateFile("main.rocket", "fn main():\n    return 0\n");
        var mapPath = temp.CreateFile("app.rocket.map.json", "{\"format\":\"rocket-source-map-1\",\"functions\":[{\"source\":\"main.rocket\"}]}");
        var map = RocketDebugSourceMap.Read(mapPath, temp.Path);

        var location = DbgEngProtocol.ParseCurrentLocation("app!main+0x2 [rocket:\\source\\main.rocket @ 3]", map, "breakpoint");

        Assert.IsNotNull(location);
        Assert.AreEqual(Path.GetFullPath(source), location.SourcePath);
        Assert.AreEqual(3, location.Line);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-dbgeng-{Guid.NewGuid():N}");
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
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
