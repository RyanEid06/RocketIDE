using RocketIDE.App.Editor;
using RocketIDE.Infrastructure.Settings;

namespace RocketIDE.App.Tests.Editor;

[TestClass]
public sealed class EditorPreferenceServiceTests
{
    [TestMethod]
    public void Zoom_ClampsAndResetRestoresBaseline()
    {
        var service = new EditorPreferenceService();
        service.Apply(new EditorPreferences { FontSize = EditorPreferences.MaximumFontSize });

        service.ZoomIn();
        Assert.AreEqual(EditorPreferences.MaximumFontSize, service.Current.FontSize);

        service.Apply(new EditorPreferences { FontSize = EditorPreferences.MinimumFontSize });
        service.ZoomOut();
        Assert.AreEqual(EditorPreferences.MinimumFontSize, service.Current.FontSize);

        service.ResetZoom();
        Assert.AreEqual(EditorPreferences.DefaultFontSize, service.Current.FontSize);
    }

    [TestMethod]
    public void WordWrapAndFormatOnSave_AreExplicitPreferences()
    {
        var service = new EditorPreferenceService();

        service.SetWordWrap(true);
        service.SetFormatOnSave(true);

        Assert.IsTrue(service.Current.WordWrap);
        Assert.IsTrue(service.Current.FormatOnSave);
    }
}
