using RocketIDE.Core.Recovery;

namespace RocketIDE.Core.Tests.Recovery;

[TestClass]
public sealed class RecoveryModelsTests
{
    [TestMethod]
    public void WindowBounds_ClampToWorkArea_MovesAndSizesWindowInsideVisibleArea()
    {
        var bounds = new WindowBounds(-4000, 5000, 3000, 2200, true);

        var clamped = bounds.ClampToWorkArea(100, 50, 1200, 800);

        Assert.AreEqual(100, clamped.Left);
        Assert.AreEqual(50, clamped.Top);
        Assert.AreEqual(1200, clamped.Width);
        Assert.AreEqual(800, clamped.Height);
        Assert.IsTrue(clamped.IsMaximized);
    }

    [TestMethod]
    public void RecoverySet_HasSnapshotsReflectsContents()
    {
        var empty = RecoverySet.Empty;
        var snapshot = new RecoverySnapshot(
            @"C:\work\main.rocket",
            "ABC",
            DateTimeOffset.UnixEpoch,
            3,
            "unsaved",
            DateTimeOffset.UtcNow);

        Assert.IsFalse(empty.HasSnapshots);
        Assert.IsTrue(new RecoverySet([snapshot], DateTimeOffset.UtcNow).HasSnapshots);
    }
}
