using Microsoft.VisualStudio.TestTools.UnitTesting;
using RocketIDE.App.Editor;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class RocketSyntaxHighlightingTests
{
    [TestMethod]
    public void Definition_LoadsValidAvalonEditHighlightingDefinition()
    {
        var definition = RocketSyntaxHighlighting.Definition;

        Assert.IsNotNull(definition);
        Assert.AreEqual("Rocket", definition.Name);
        Assert.AreEqual("1", definition.Properties["RocketIDE.SyntaxVersion"]);
    }
}
