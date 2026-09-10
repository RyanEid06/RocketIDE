using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RocketIDE.Core.Tests;

[TestClass]
public sealed class BaselineTests
{
    [TestMethod]
    public void TestProjectReferencesCoreAssembly()
    {
        Assert.AreEqual("RocketIDE.Core", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
