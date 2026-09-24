using RocketIDE.App.Integration;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Tests.Integration;

[TestClass]
public sealed class FormatOnSaveServiceTests
{
    [TestMethod]
    public async Task PrepareAsync_AppliesFormatterWhenSnapshotIsStillCurrent()
    {
        var output = new List<string>();
        var service = new FormatOnSaveService(output.Add);
        var current = new RocketSessionDocument("main.rocket", "old", 2, "main.rocket");
        var edit = new RocketTextEdit(
            new LspRange(new LspPosition(0, 0), new LspPosition(0, 3)),
            "new");

        var prepared = await service.PrepareAsync(
            current,
            () => current,
            _ => Task.FromResult<IReadOnlyList<RocketTextEdit>?>([edit]),
            (_, _) =>
            {
                current = new RocketSessionDocument("main.rocket", "new", 3, "main.rocket");
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.AreEqual(3, prepared.Version);
        Assert.AreEqual("new", prepared.Text);
        Assert.AreEqual(0, output.Count);
    }

    [TestMethod]
    public async Task PrepareAsync_StaleResponseNeverAppliesAndReturnsLatestEditorState()
    {
        var output = new List<string>();
        var service = new FormatOnSaveService(output.Add);
        var requested = new RocketSessionDocument("main.rocket", "old", 2, "main.rocket");
        var current = requested;
        var applyCalls = 0;

        var prepared = await service.PrepareAsync(
            requested,
            () => current,
            _ =>
            {
                current = new RocketSessionDocument("main.rocket", "user edit", 3, "main.rocket");
                return Task.FromResult<IReadOnlyList<RocketTextEdit>?>([]);
            },
            (_, _) => { applyCalls++; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.AreEqual(0, applyCalls);
        Assert.AreEqual(3, prepared.Version);
        Assert.AreEqual("user edit", prepared.Text);
        Assert.IsTrue(output.Any(line => line.Contains("stale", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task PrepareAsync_UnsupportedFormatterReturnsUnformattedContents()
    {
        var output = new List<string>();
        var service = new FormatOnSaveService(output.Add);
        var current = new RocketSessionDocument("main.rocket", "keep me", 4, "main.rocket");

        var prepared = await service.PrepareAsync(
            current,
            () => current,
            _ => Task.FromResult<IReadOnlyList<RocketTextEdit>?>(null),
            (_, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.AreEqual(current, prepared);
        Assert.IsTrue(output.Any(line => line.Contains("unavailable", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task PrepareAsync_InvalidEditFailureIsBestEffortAndKeepsLatestContents()
    {
        var output = new List<string>();
        var service = new FormatOnSaveService(output.Add);
        var current = new RocketSessionDocument("main.rocket", "keep me", 4, "main.rocket");
        var edit = new RocketTextEdit(
            new LspRange(new LspPosition(99, 0), new LspPosition(99, 1)),
            "bad");

        var prepared = await service.PrepareAsync(
            current,
            () => current,
            _ => Task.FromResult<IReadOnlyList<RocketTextEdit>?>([edit]),
            (_, _) => throw new WorkspaceEditValidationException("invalid formatter edit"),
            CancellationToken.None);

        Assert.AreEqual(current, prepared);
        Assert.IsTrue(output.Any(line => line.Contains("invalid formatter edit", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PrepareAsync_UserCancellationPropagates()
    {
        var service = new FormatOnSaveService(_ => { });
        var current = new RocketSessionDocument("main.rocket", "keep me", 4, "main.rocket");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.PrepareAsync(
            current,
            () => current,
            token => Task.FromCanceled<IReadOnlyList<RocketTextEdit>?>(token),
            (_, _) => Task.CompletedTask,
            cancellation.Token));
    }
}
