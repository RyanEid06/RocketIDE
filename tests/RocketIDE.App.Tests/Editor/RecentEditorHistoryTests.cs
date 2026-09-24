using System.IO;
using RocketIDE.App.Editor;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class RecentEditorHistoryTests
{
    [TestMethod]
    public void Record_IsMostRecentFirstDeduplicatedAndBounded()
    {
        var history = new RecentEditorHistory(2);
        var a = Path.GetFullPath("a.rocket");
        var b = Path.GetFullPath("b.rocket");
        var c = Path.GetFullPath("c.rocket");

        history.Record(a);
        history.Record(b);
        history.Record(a);
        CollectionAssert.AreEqual(new[] { a, b }, history.Snapshot.ToArray());

        history.Record(c);
        CollectionAssert.AreEqual(new[] { c, a }, history.Snapshot.ToArray());
    }
}
