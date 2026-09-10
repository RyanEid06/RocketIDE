using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RocketIDE.Infrastructure.Tests;

[TestClass]
public sealed class BaselineTests
{
    [TestMethod]
    public void TestProjectReferencesInfrastructureAssembly()
    {
        Assert.AreEqual("RocketIDE.Infrastructure", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
