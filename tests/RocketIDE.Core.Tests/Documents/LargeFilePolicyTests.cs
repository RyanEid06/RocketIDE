using System.Text;
using RocketIDE.Core.Documents;

namespace RocketIDE.Core.Tests.Documents;

[TestClass]
public sealed class LargeFilePolicyTests
{
    [TestMethod]
    public void Decide_UsesUtf8ByteCountAndAllowsExactLspBoundary()
    {
        var exactText = new string('a', (int)LargeFilePolicy.MaxLspDocumentBytes);
        var exactBytes = Encoding.UTF8.GetByteCount(exactText);

        var decision = LargeFilePolicy.Decide(exactBytes);

        Assert.IsFalse(decision.IsLargeFileMode);
        Assert.IsTrue(decision.AllowLsp);
        Assert.IsTrue(decision.AllowLocalEditing);
        Assert.AreEqual(LargeFilePolicy.MaxLspDocumentBytes, decision.ByteLength);
    }

    [TestMethod]
    public void Decide_DisablesLspAboveLimitButKeepsLocalEditingAndFindAvailable()
    {
        var decision = LargeFilePolicy.Decide(LargeFilePolicy.MaxLspDocumentBytes + 1);

        Assert.IsTrue(decision.IsLargeFileMode);
        Assert.IsFalse(decision.AllowLsp);
        Assert.IsTrue(decision.AllowLocalEditing);
        Assert.IsTrue(decision.AllowFindAndGoto);
        StringAssert.Contains(decision.Reason, "4 MiB");
    }

    [TestMethod]
    public void Decide_RejectsNegativeByteLength()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LargeFilePolicy.Decide(-1));
    }
}
