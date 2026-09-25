using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class OutputViewModelTests
{
    [TestMethod]
    public void AppendMany_PreservesTailAndRaisesOneCollectionChangeForLargeBatch()
    {
        var viewModel = new OutputViewModel(maxLines: 100);
        var changes = 0;
        viewModel.Lines.CollectionChanged += (_, _) => changes++;

        viewModel.AppendMany(Enumerable.Range(0, 1000).Select(number => number.ToString()));

        Assert.AreEqual(100, viewModel.Lines.Count);
        Assert.AreEqual("900", viewModel.Lines[0]);
        Assert.AreEqual("999", viewModel.Lines[^1]);
        Assert.IsTrue(changes <= 2, $"Expected chunked collection notification, got {changes}.");
    }

    [TestMethod]
    public async Task OutputBuffer_BatchesDispatcherPostsAndStopsAtBoundedHistory()
    {
        var scheduled = new Queue<Action>();
        var viewModel = new OutputViewModel(maxLines: 50);
        var buffer = new UiOutputBuffer(viewModel, scheduled.Enqueue, maxPending: 100, batchSize: 25);
        var changes = 0;
        viewModel.Lines.CollectionChanged += (_, _) => changes++;

        for (var number = 0; number < 1000; number++)
        {
            buffer.Enqueue(number.ToString());
        }
        Assert.AreEqual(1, scheduled.Count);
        while (scheduled.Count > 0)
        {
            scheduled.Dequeue()();
        }
        await buffer.FlushAsync(CancellationToken.None);

        Assert.AreEqual(50, viewModel.Lines.Count);
        Assert.AreEqual("950", viewModel.Lines[0]);
        Assert.AreEqual("999", viewModel.Lines[^1]);
        Assert.IsTrue(changes <= 8, $"Expected batches, got {changes} collection changes.");
    }

    [TestMethod]
    public void Append_PreservesLinesAndCapsHistory()
    {
        var viewModel = new OutputViewModel(maxLines: 3);

        viewModel.Append("one");
        viewModel.Append("two");
        viewModel.Append("three");
        viewModel.Append("four");

        CollectionAssert.AreEqual(new[] { "two", "three", "four" }, viewModel.Lines.ToArray());
    }

    [TestMethod]
    public void BeginCommand_AddsVisibleBoundaryWithoutClearingExistingHistory()
    {
        var viewModel = new OutputViewModel();
        viewModel.Append("old output");

        viewModel.BeginCommand("Build", "C:\\work\\demo");

        Assert.AreEqual("old output", viewModel.Lines[0]);
        StringAssert.Contains(viewModel.Lines[^1], "Build");
        StringAssert.Contains(viewModel.Lines[^1], "C:\\work\\demo");
    }

    [TestMethod]
    public void OutputBuffer_OrdersHeadersAndClearAgainstQueuedLines()
    {
        var scheduled = new Queue<Action>();
        var viewModel = new OutputViewModel();
        var buffer = new UiOutputBuffer(viewModel, scheduled.Enqueue);
        buffer.Enqueue("earlier");
        buffer.BeginCommand("Build", "target");
        while (scheduled.Count > 0) scheduled.Dequeue()();
        CollectionAssert.AreEqual(new[] { "earlier", "=== Rocket Build: target ===" }, viewModel.Lines.ToArray());

        buffer.Enqueue("stale");
        buffer.Clear();
        buffer.Enqueue("fresh");
        while (scheduled.Count > 0) scheduled.Dequeue()();
        CollectionAssert.AreEqual(new[] { "fresh" }, viewModel.Lines.ToArray());
    }
}
