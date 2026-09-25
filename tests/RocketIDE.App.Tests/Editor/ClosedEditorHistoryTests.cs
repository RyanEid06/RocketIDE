using System.IO;
using RocketIDE.App.Editor;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class ClosedEditorHistoryTests
{
    [TestMethod]
    public void RecordAndPop_UsesMostRecentOrderAndDeduplicates()
    {
        var history = new ClosedEditorHistory(3);
        var a = Path.GetFullPath("a.rocket");
        var b = Path.GetFullPath("b.rocket");

        history.Record(a);
        history.Record(b);
        history.Record(a);

        CollectionAssert.AreEqual(new[] { a, b }, history.Snapshot.ToArray());
        Assert.IsTrue(history.TryPop(out var first));
        Assert.AreEqual(a, first);
        Assert.IsTrue(history.TryPop(out var second));
        Assert.AreEqual(b, second);
        Assert.IsFalse(history.TryPop(out _));
    }

    [TestMethod]
    public void Record_RespectsCapacity()
    {
        var history = new ClosedEditorHistory(2);
        history.Record("a.rocket");
        history.Record("b.rocket");
        history.Record("c.rocket");

        Assert.AreEqual(2, history.Count);
        Assert.IsTrue(history.Snapshot[0].EndsWith("c.rocket", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(history.Snapshot[1].EndsWith("b.rocket", StringComparison.OrdinalIgnoreCase));
    }
}
