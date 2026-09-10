using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
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
    [TestMethod]
    public void Definition_HighlightsRepresentativeRocketSourceWithoutZeroLengthRules()
    {
        const string source = """
            # full-line comment
            fn main() -> Int:
                let message = "# inside string"
                let value = 42 # trailing comment
                return value
            """;

        var document = new TextDocument(source);
        using var highlighter = new DocumentHighlighter(document, RocketSyntaxHighlighting.Definition);

        for (var line = 1; line <= document.LineCount; line++)
        {
            var highlighted = highlighter.HighlightLine(line);
            Assert.IsNotNull(highlighted);
        }
    }

}
