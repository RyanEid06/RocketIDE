using System.IO;
using RocketIDE.App.ViewModels;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class ReferencesViewModelTests
{
    [TestMethod]
    public void SetResults_PreservesOnlyServerLocationsAndUsesOneBasedPresentation()
    {
        var model = new ReferencesViewModel();
        var path = Path.Combine(Path.GetTempPath(), "src", "main.rocket");
        model.SetResults("References", [new RocketLocation(path, new LspRange(new LspPosition(2, 4), new LspPosition(2, 8)))]);

        var item = model.Items.Single();
        Assert.AreEqual(path, item.FilePath);
        Assert.AreEqual(3, item.Line);
        Assert.AreEqual(5, item.Column);
        Assert.AreEqual("REFERENCES (1)", model.HeaderText);
    }
}
