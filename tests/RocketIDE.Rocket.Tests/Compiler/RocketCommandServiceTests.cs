using RocketIDE.Core.Commands;
using RocketIDE.Core.Output;
using RocketIDE.Rocket.Compiler;
using RocketIDE.Rocket.Projects;
using RocketIDE.Rocket.Tools;

namespace RocketIDE.Rocket.Tests.Compiler;

[TestClass]
public sealed class RocketCommandServiceTests
{
    [TestMethod]
    public async Task ExecuteAsync_ParsesStructuredStdoutAndPreservesPlainAndStderrOutput()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        File.WriteAllText(source, "fn main() -> Int:\n    return 0\n");
        var compiler = Path.Combine(temp.Path, "rocketc.exe");
        File.WriteAllText(compiler, string.Empty);
        var runner = new FakeProcessRunner([
            new ProcessOutput("{\"schema\":\"rocket-message-1\",\"reason\":\"build-finished\",\"command\":\"build\",\"success\":true,\"artifact\":\"demo.exe\"}", ProcessOutputStream.StandardOutput),
            new ProcessOutput("plain stdout", ProcessOutputStream.StandardOutput),
            new ProcessOutput("{\"schema\":\"rocket-message-2\",\"reason\":\"build-finished\"}", ProcessOutputStream.StandardOutput),
            new ProcessOutput("warning stderr", ProcessOutputStream.StandardError),
            new ProcessOutput("{\"schema\":\"rocket-message-1\",\"reason\":\"test-summary\",\"passed\":99}", ProcessOutputStream.StandardError),
        ]);
        var service = new RocketCommandService(runner, new FakeLocator(compiler), new RocketTargetDiscovery());
        var events = new List<RocketCommandOutput>();

        var result = await service.ExecuteAsync(RocketCommandKind.Build, source, [], new InlineProgress<RocketCommandOutput>(events.Add), CancellationToken.None);

        Assert.AreEqual(0, result.ProcessResult.ExitCode);
        Assert.IsTrue(events.Any(item => item.Message?.Reason == "build-finished" && item.DisplayText.Contains("demo.exe", StringComparison.Ordinal)));
        Assert.IsTrue(events.Any(item => item.Message is null && item.DisplayText == "plain stdout"));
        Assert.IsTrue(events.Any(item => item.Message is null && item.DisplayText == "{\"schema\":\"rocket-message-2\",\"reason\":\"build-finished\"}"));
        Assert.IsTrue(events.Any(item => item.Stream == ProcessOutputStream.StandardError && item.DisplayText == "warning stderr"));
        Assert.IsTrue(events.Any(item =>
            item.Stream == ProcessOutputStream.StandardError &&
            item.Message is null &&
            item.DisplayText.Contains("\"passed\":99", StringComparison.Ordinal)));
    }


    [TestMethod]
    public async Task ExecuteAsync_UsesValidatedCompilerEvenWhenLanguageServerIsUnavailable()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        File.WriteAllText(source, "fn main() -> Int:\n    return 0\n");
        var compiler = Path.Combine(temp.Path, "rocketc.exe");
        File.WriteAllText(compiler, string.Empty);
        var runner = new FakeProcessRunner([]);
        var service = new RocketCommandService(runner, new CompilerOnlyLocator(compiler), new RocketTargetDiscovery());

        var result = await service.ExecuteAsync(
            RocketCommandKind.Check,
            source,
            [],
            new InlineProgress<RocketCommandOutput>(_ => { }),
            CancellationToken.None);

        Assert.AreEqual(0, result.ProcessResult.ExitCode);
    }

    [TestMethod]
    public async Task ExecuteAsync_RejectsConcurrentCommandExplicitly()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "main.rocket");
        File.WriteAllText(source, "fn main() -> Int:\n    return 0\n");
        var compiler = Path.Combine(temp.Path, "rocketc.exe");
        File.WriteAllText(compiler, string.Empty);
        var runner = new BlockingProcessRunner();
        var service = new RocketCommandService(runner, new FakeLocator(compiler), new RocketTargetDiscovery());
        var first = service.ExecuteAsync(RocketCommandKind.Build, source, [], new InlineProgress<RocketCommandOutput>(_ => { }), CancellationToken.None);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ExecuteAsync(
            RocketCommandKind.Test,
            source,
            [],
            new InlineProgress<RocketCommandOutput>(_ => { }),
            CancellationToken.None));

        service.StopActive();
        await first.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class FakeProcessRunner(IReadOnlyList<ProcessOutput> outputs) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessStartRequest request, IProgress<ProcessOutput> output, CancellationToken cancellationToken)
        {
            foreach (var item in outputs) output.Report(item);
            return Task.FromResult(new ProcessRunResult(0, false));
        }

        public void StopActive() { }
    }

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        private readonly TaskCompletionSource<ProcessRunResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProcessRunResult> RunAsync(ProcessStartRequest request, IProgress<ProcessOutput> output, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _completion.Task;
        }

        public void StopActive() => _completion.TrySetResult(new ProcessRunResult(unchecked((int)0xC000013A), true));
    }


    private sealed class CompilerOnlyLocator(string compilerPath) : IRocketToolLocator
    {
        public Task<RocketToolchain?> LocateAsync(string? activePath, CancellationToken cancellationToken) =>
            Task.FromResult<RocketToolchain?>(null);

        public Task<RocketToolDiscoveryResult> DiscoverAsync(string? activePath, CancellationToken cancellationToken) =>
            Task.FromResult(new RocketToolDiscoveryResult(compilerPath, null, "rocketc test", null, ["rocket-lsp.exe was not found"]));
    }

    private sealed class FakeLocator(string compilerPath) : IRocketToolLocator
    {
        public Task<RocketToolchain?> LocateAsync(string? activePath, CancellationToken cancellationToken) =>
            Task.FromResult<RocketToolchain?>(new RocketToolchain(compilerPath, Path.Combine(Path.GetDirectoryName(compilerPath)!, "rocket-lsp.exe"), "rocketc test", "rocket-lsp test"));

        public Task<RocketToolDiscoveryResult> DiscoverAsync(string? activePath, CancellationToken cancellationToken) =>
            Task.FromResult(new RocketToolDiscoveryResult(compilerPath, Path.Combine(Path.GetDirectoryName(compilerPath)!, "rocket-lsp.exe"), "rocketc test", "rocket-lsp test", []));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-command-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
