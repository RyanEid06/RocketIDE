using System.Text.Json;
using RocketIDE.Rocket.LanguageServer;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.Rocket.Tests.LanguageServer.Features;

[TestClass]
public sealed class NavigationClientTests
{
    [TestMethod]
    public void ParseDefinition_AcceptsLocationsAndLocationLinksButOnlyFileUris()
    {
        var target = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rocketide-nav", "target.rocket"));
        var uri = new Uri(target).AbsoluteUri;
        using var document = JsonDocument.Parse($$"""
        [
          { "uri": {{JsonSerializer.Serialize(uri)}}, "range": { "start": {"line":1,"character":2}, "end": {"line":1,"character":5} } },
          { "targetUri": {{JsonSerializer.Serialize(uri)}}, "targetRange": { "start": {"line":4,"character":0}, "end": {"line":4,"character":9} }, "targetSelectionRange": { "start": {"line":4,"character":3}, "end": {"line":4,"character":7} } }
        ]
        """);

        var result = NavigationClient.ParseDefinitionResponse(document.RootElement);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(target, result[0].Path);
        Assert.AreEqual(1, result[0].Range.Start.Line);
        Assert.AreEqual(3, result[1].Range.Start.Character);
    }

    [TestMethod]
    public void ParseDefinition_RejectsMalformedOrNonFileUri()
    {
        using var document = JsonDocument.Parse("""
        { "uri": "https://example.com/main.rocket", "range": { "start": {"line":0,"character":0}, "end": {"line":0,"character":1} } }
        """);

        Assert.ThrowsExactly<LspProtocolException>(() => NavigationClient.ParseDefinitionResponse(document.RootElement));
    }

    [TestMethod]
    public void ParsePrepareRename_HandlesRangePlaceholderDefaultBehaviorAndNull()
    {
        using var range = JsonDocument.Parse("""{ "range": { "start": {"line":2,"character":4}, "end": {"line":2,"character":8} }, "placeholder": "name" }""");
        using var defaultBehavior = JsonDocument.Parse("""{ "defaultBehavior": true }""");
        using var rejected = JsonDocument.Parse("null");

        var prepared = NavigationClient.ParsePrepareRenameResponse(range.RootElement);
        var defaultPrepared = NavigationClient.ParsePrepareRenameResponse(defaultBehavior.RootElement);
        var none = NavigationClient.ParsePrepareRenameResponse(rejected.RootElement);

        Assert.IsNotNull(prepared);
        Assert.AreEqual("name", prepared.Placeholder);
        Assert.IsFalse(prepared.DefaultBehavior);
        Assert.IsNotNull(defaultPrepared);
        Assert.IsTrue(defaultPrepared.DefaultBehavior);
        Assert.IsNull(none);
    }

    [TestMethod]
    [DataRow("create")]
    [DataRow("rename")]
    [DataRow("delete")]
    public void ParseWorkspaceEdit_RejectsResourceOperationsInsteadOfIgnoringThem(string kind)
    {
        using var document = JsonDocument.Parse($$"""
        {
          "documentChanges": [
            { "kind": {{JsonSerializer.Serialize(kind)}}, "uri": "file:///tmp/new.rocket" }
          ]
        }
        """);

        var exception = Assert.ThrowsExactly<LspProtocolException>(() => NavigationClient.ParseWorkspaceEdit(document.RootElement));
        StringAssert.Contains(exception.Message, "resource operation");
    }

    [TestMethod]
    public void ParseWorkspaceEdit_RejectsMalformedUriBeforeAnyApplyStage()
    {
        using var document = JsonDocument.Parse("""
        {
          "changes": { "not a uri": [ { "range": { "start": {"line":0,"character":0}, "end": {"line":0,"character":0} }, "newText": "x" } ] }
        }
        """);

        Assert.ThrowsExactly<LspProtocolException>(() => NavigationClient.ParseWorkspaceEdit(document.RootElement));
    }

    [TestMethod]
    public void ParseCodeActions_MarksCommandsUnsupportedAndKeepsEditOnlyActions()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rocketide-actions", "main.rocket"));
        var uri = new Uri(path).AbsoluteUri;
        using var document = JsonDocument.Parse($$"""
        [
          {
            "title": "Replace typo",
            "kind": "quickfix",
            "edit": { "changes": { {{JsonSerializer.Serialize(uri)}}: [ { "range": { "start": {"line":0,"character":0}, "end": {"line":0,"character":3} }, "newText": "print" } ] } }
          },
          { "title": "Run arbitrary command", "command": "rocket.doAnything", "arguments": ["x"] }
        ]
        """);

        var actions = NavigationClient.ParseCodeActions(document.RootElement);

        Assert.AreEqual(2, actions.Count);
        Assert.IsNotNull(actions[0].Edit);
        Assert.IsFalse(actions[0].HasUnsupportedCommand);
        Assert.IsNull(actions[1].Edit);
        Assert.IsTrue(actions[1].HasUnsupportedCommand);
    }

    [TestMethod]
    public void ParseCodeActions_PreservesServerDisabledReason()
    {
        using var document = JsonDocument.Parse("""
        [
          { "title": "Fix name", "kind": "quickfix", "disabled": { "reason": "No longer applicable" } }
        ]
        """);

        var action = NavigationClient.ParseCodeActions(document.RootElement).Single();

        Assert.AreEqual("No longer applicable", action.DisabledReason);
    }

    [TestMethod]
    public void IsRequestedCodeActionKind_RejectsFormattingAndAcceptsQuickFixHierarchy()
    {
        Assert.IsTrue(NavigationClient.IsRequestedCodeActionKind("quickfix", "quickfix"));
        Assert.IsTrue(NavigationClient.IsRequestedCodeActionKind("quickfix.rocket", "quickfix"));
        Assert.IsFalse(NavigationClient.IsRequestedCodeActionKind("source.format", "quickfix"));
        Assert.IsFalse(NavigationClient.IsRequestedCodeActionKind(null, "quickfix"));
    }

    [TestMethod]
    public void ParseFileUri_RejectsFileUrisWithQueryOrFragment()
    {
        Assert.ThrowsExactly<LspProtocolException>(() => NavigationClient.ParseFileUri("file:///tmp/main.rocket?x=1", "test"));
        Assert.ThrowsExactly<LspProtocolException>(() => NavigationClient.ParseFileUri("file:///tmp/main.rocket#frag", "test"));
    }
}
