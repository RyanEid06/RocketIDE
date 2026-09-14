using RocketIDE.Infrastructure.Processes;

namespace RocketIDE.Infrastructure.Tests.Processes;

[TestClass]
public sealed class WindowsCommandLineTests
{
    [TestMethod]
    public void QuoteArgument_PreservesSimpleArgument()
    {
        Assert.AreEqual("hello", WindowsCommandLine.QuoteArgument("hello"));
    }

    [TestMethod]
    public void QuoteArgument_QuotesWhitespaceEmptyAndEmbeddedQuotes()
    {
        Assert.AreEqual("\"\"", WindowsCommandLine.QuoteArgument(string.Empty));
        Assert.AreEqual("\"hello world\"", WindowsCommandLine.QuoteArgument("hello world"));
        Assert.AreEqual("\"a\\\\\\\"b\"", WindowsCommandLine.QuoteArgument("a\\\"b"));
    }

    [TestMethod]
    public void JoinArguments_UsesWindowsCommandLineEscaping()
    {
        Assert.AreEqual("build \"C:\\work space\\main.rocket\" --message-format=json", WindowsCommandLine.JoinArguments([
            "build",
            @"C:\work space\main.rocket",
            "--message-format=json",
        ]));
    }

    [TestMethod]
    public void ParseArguments_RoundTripsQuotedProgramArguments()
    {
        var parsed = WindowsCommandLine.ParseArguments("alpha \"two words\" \"quote\\\"inside\" tail");

        CollectionAssert.AreEqual(new[] { "alpha", "two words", "quote\"inside", "tail" }, parsed.ToArray());
    }
}
