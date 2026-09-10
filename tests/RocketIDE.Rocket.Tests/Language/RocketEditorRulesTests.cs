using RocketIDE.Rocket.Language;

namespace RocketIDE.Rocket.Tests.Language;

[TestClass]
public sealed class RocketEditorRulesTests
{
    [DataTestMethod]
    [DataRow("fn main() -> Int:", 4)]
    [DataRow("    if ready: # comment", 8)]
    [DataRow("    let text = \"value:\"", 4)]
    [DataRow("        return 1", 8)]
    public void GetNewLineIndentation_UsesFourSpaceRocketBlocks(string line, int expectedSpaces)
    {
        Assert.AreEqual(expectedSpaces, RocketEditorRules.GetNewLineIndentation(line));
    }

    [DataTestMethod]
    [DataRow("else:", true)]
    [DataRow("case value:", true)]
    [DataRow("  case value: # comment", true)]
    [DataRow("if value:", false)]
    public void IsDedentKeyword_RecognizesElseAndCase(string line, bool expected)
    {
        Assert.AreEqual(expected, RocketEditorRules.IsDedentKeyword(line));
    }


    [DataTestMethod]
    [DataRow("        else:", "        return 1", 4)]
    [DataRow("    else:", "        return 1", 0)]
    [DataRow("        case value:", "        return 1", 4)]
    [DataRow("        if ready:", "        return 1", 0)]
    [DataRow("else:", "return 1", 0)]
    public void GetCurrentLineDedent_OnlyRemovesAutomaticBodyIndentForElseAndCase(
        string currentLine,
        string previousNonBlankLine,
        int expectedSpaces)
    {
        Assert.AreEqual(expectedSpaces, RocketEditorRules.GetCurrentLineDedent(currentLine, previousNonBlankLine));
    }

    [TestMethod]
    public void PairingRules_WrapSelectionAndRecognizeBalancedPair()
    {
        Assert.AreEqual("(value)", RocketEditorRules.WrapSelection("value", '('));
        Assert.AreEqual("\"value\"", RocketEditorRules.WrapSelection("value", '"'));
        Assert.IsTrue(RocketEditorRules.IsAutoClosePair('(', ')'));
        Assert.IsTrue(RocketEditorRules.IsAutoClosePair('[', ']'));
        Assert.IsFalse(RocketEditorRules.IsAutoClosePair('(', ']'));
    }

    [TestMethod]
    public void IsInStringOrComment_HandlesEscapesAndComments()
    {
        Assert.IsTrue(RocketEditorRules.IsInStringOrComment("let x = \"hello", "let x = \"hello".Length));
        Assert.IsFalse(RocketEditorRules.IsInStringOrComment("let x = \"hello\"", "let x = \"hello\"".Length));
        Assert.IsTrue(RocketEditorRules.IsInStringOrComment("let x = 1 # comment", "let x = 1 # comment".Length));
        Assert.IsFalse(RocketEditorRules.IsInStringOrComment("let x = \"#\"", "let x = \"#\"".Length));
    }
}
