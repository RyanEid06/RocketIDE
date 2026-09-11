using System.IO;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Editor;
using RocketIDE.Core.Diagnostics;

namespace RocketIDE.App.Tests;

[TestClass]
public sealed class DiagnosticRendererTests
{
    [TestMethod]
    public void TryGetOffsetRange_UsesUtf16CodeUnitsForSurrogatePairs()
    {
        var document = new TextDocument("a🚀b\nnext");

        Assert.IsTrue(DiagnosticRenderer.TryGetOffsetRange(document, new SourceRange(0, 1, 0, 3), out var start, out var length));

        Assert.AreEqual(1, start);
        Assert.AreEqual(2, length);
        Assert.AreEqual("🚀", document.GetText(start, length));
    }


    [TestMethod]
    public void TryGetOffsetRange_RejectsCharacterPositionThatSplitsSurrogatePair()
    {
        var document = new TextDocument("a🚀b\n");

        Assert.IsFalse(DiagnosticRenderer.TryGetOffsetRange(document, new SourceRange(0, 2, 0, 3), out _, out _));
    }

    [TestMethod]
    public void MarkerCollection_ReplacesAndClearsDiagnosticMarkers()
    {
        var path = Path.GetFullPath("markers.rocket");
        var document = new TextDocument("abc\n");
        var markers = new DiagnosticMarkerCollection();
        var first = new RocketDiagnostic("rocketc", "R1", "first", DiagnosticSeverity.Error, path, new SourceRange(0, 0, 0, 1));
        var second = new RocketDiagnostic("rocketc", "R2", "second", DiagnosticSeverity.Warning, path, new SourceRange(0, 1, 0, 2));

        markers.Update(document, [first]);
        Assert.AreEqual(1, markers.Markers.Count);
        Assert.AreEqual("R1", markers.Markers[0].Diagnostic.Code);

        markers.Update(document, [second]);
        Assert.AreEqual(1, markers.Markers.Count);
        Assert.AreEqual("R2", markers.Markers[0].Diagnostic.Code);

        markers.Clear();
        Assert.AreEqual(0, markers.Markers.Count);
    }

    [TestMethod]
    public void TryGetOffsetRange_MapsMultiLineRangeWithoutMutatingDocument()
    {
        var original = "first\nsecond\nthird";
        var document = new TextDocument(original);

        Assert.IsTrue(DiagnosticRenderer.TryGetOffsetRange(document, new SourceRange(0, 3, 1, 3), out var start, out var length));

        Assert.AreEqual("st\nsec", document.GetText(start, length));
        Assert.AreEqual(original, document.Text);
    }

    [TestMethod]
    public void TryGetOffsetRange_RejectsInvalidLineOrReversedRange()
    {
        var document = new TextDocument("abc\n");

        Assert.IsFalse(DiagnosticRenderer.TryGetOffsetRange(document, new SourceRange(3, 0, 3, 1), out _, out _));
        Assert.IsFalse(DiagnosticRenderer.TryGetOffsetRange(document, new SourceRange(0, 2, 0, 1), out _, out _));
    }
}
