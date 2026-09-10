using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RocketIDE.Rocket.Tests;

[TestClass]
public sealed class BaselineTests
{
    [TestMethod]
    public void TestProjectReferencesRocketAssembly()
    {
        Assert.AreEqual("RocketIDE.Rocket", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
