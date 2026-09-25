using System.IO;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.LanguageServer;

namespace RocketIDE.App.Tests.Wp03;

[TestClass]
public sealed class SymbolSearchViewModelTests
{
    [TestMethod]
    public async Task FuzzyQueryRanksAuthoritativeCandidatesWhenServerReturnsThem()
    {
        var path = Path.GetFullPath("symbols.rocket");
        var range = new SourceRange(0, 0, 0, 1);
        using var search = new SymbolSearchViewModel((_, _) => Task.FromResult<IReadOnlyList<SymbolSearchItem>>(
        [
            new SymbolSearchItem("PlayerController", "", "Class", path, range),
            new SymbolSearchItem("Other", "", "Class", path, range),
        ]));

        search.Query = "plctrl";
        await WaitUntilAsync(() => search.Items.Count > 0);

        Assert.AreEqual("PlayerController", search.Items[0].Name);
        Assert.AreEqual(1, search.Items.Count);
    }

    [TestMethod]
    public async Task IncompleteProtocolResponseIsShownAsFailure()
    {
        using var search = new SymbolSearchViewModel((_, _) =>
            Task.FromException<IReadOnlyList<SymbolSearchItem>>(
                new LspProtocolException("workspace/symbol response is incomplete")));

        await WaitUntilAsync(() => search.StatusText.StartsWith("Symbol search failed:", StringComparison.Ordinal));

        Assert.AreEqual(0, search.Items.Count);
        StringAssert.Contains(search.StatusText, "incomplete");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
