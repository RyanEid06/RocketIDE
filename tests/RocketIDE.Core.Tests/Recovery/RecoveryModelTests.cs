using RocketIDE.Core.Recovery;

namespace RocketIDE.Core.Tests.Recovery;

[TestClass]
public sealed class RecoveryModelTests
{
    [TestMethod]
    public void WindowBounds_ClampOffScreenGeometryToWorkArea()
    {
        var bounds = new WindowBounds(-900, 1900, 2200, 1400, IsMaximized: true);

        var clamped = bounds.ClampToWorkArea(0, 0, 1920, 1080);

        Assert.AreEqual(0, clamped.Left);
        Assert.AreEqual(0, clamped.Top);
        Assert.AreEqual(1920, clamped.Width);
        Assert.AreEqual(1080, clamped.Height);
        Assert.IsTrue(clamped.IsMaximized);
    }

    [TestMethod]
    public void RecoverySnapshot_ReportsConflictWithoutChangingSourceText()
    {
        var snapshot = new RecoverySnapshot(
            @"C:\work\main.rocket",
            "old",
            DateTimeOffset.UtcNow,
            3,
            "unsaved",
            DateTimeOffset.UtcNow,
            "new",
            HasDiskConflict: true,
            "The file changed on disk.");

        Assert.IsTrue(snapshot.IsConflict);
        Assert.AreEqual("unsaved", snapshot.Text);
        StringAssert.Contains(snapshot.ConflictMessage!, "changed");
    }
}
