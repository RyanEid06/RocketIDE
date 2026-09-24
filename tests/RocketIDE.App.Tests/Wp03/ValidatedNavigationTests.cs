using System.IO;
using RocketIDE.App.Editor;
using RocketIDE.App.Integration;
using RocketIDE.Core.Diagnostics;

namespace RocketIDE.App.Tests.Wp03;

[TestClass]
public sealed class ValidatedNavigationTests
{
    [TestMethod]
    public async Task InvalidUtf16Range_IsRejectedBeforeRouting()
    {
        var inner = new RecordingNavigation();
        var reported = string.Empty;
        var navigation = new ValidatedEditorNavigation(
            inner,
            (_, _) => Task.FromResult<string?>("abc\n"),
            message => reported = message);

        var result = await navigation.OpenOrRevealAsync(
            Path.GetFullPath("invalid.rocket"),
            new SourceRange(9, 0, 9, 1),
            CancellationToken.None);

        Assert.IsNull(result);
        Assert.AreEqual(0, inner.Calls);
        StringAssert.Contains(reported, "invalid UTF-16 range");
    }

    private sealed class RecordingNavigation : IEditorNavigation
    {
        public int Calls { get; private set; }
        public Task<IEditorViewContext?> OpenOrRevealAsync(string path, SourceRange? range = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IEditorViewContext?>(null);
        }
    }
}
