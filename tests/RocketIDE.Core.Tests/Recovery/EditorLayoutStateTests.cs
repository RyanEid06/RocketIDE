using System.Text.Json;
using RocketIDE.Core.Recovery;

namespace RocketIDE.Core.Tests.Recovery;

[TestClass]
public sealed class EditorLayoutStateTests
{
    [TestMethod]
    public void SessionState_BackwardCompatibleWhenEditorLayoutIsMissing()
    {
        const string json = """{"openDocumentPaths":["C:\\work\\main.rocket"],"activeDocumentPath":"C:\\work\\main.rocket"}""";

        var state = JsonSerializer.Deserialize<SessionState>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.IsNotNull(state);
        Assert.IsNull(state.EditorLayout);
        Assert.AreEqual(1, state.OpenDocumentPaths.Count);
    }

    [TestMethod]
    public void EditorLayoutState_RoundTripsTwoGroupsAndViewState()
    {
        var state = new SessionState
        {
            EditorLayout = new EditorLayoutState
            {
                SplitOrientation = "Vertical",
                ActiveGroupId = "g2",
                ActiveViewId = "v2",
                Groups =
                [
                    new EditorGroupState
                    {
                        GroupId = "g1",
                        SelectedViewId = "v1",
                        Views = [new EditorViewState { ViewId = "v1", DocumentPath = @"C:\a.rocket", CaretOffset = 3 }],
                    },
                    new EditorGroupState
                    {
                        GroupId = "g2",
                        SelectedViewId = "v2",
                        Views = [new EditorViewState { ViewId = "v2", DocumentPath = @"C:\b.rocket", VerticalOffset = 42 }],
                    },
                ],
            },
        };

        var json = JsonSerializer.Serialize(state);
        var restored = JsonSerializer.Deserialize<SessionState>(json);

        Assert.IsNotNull(restored?.EditorLayout);
        Assert.AreEqual("Vertical", restored.EditorLayout.SplitOrientation);
        Assert.AreEqual(2, restored.EditorLayout.Groups.Count);
        Assert.AreEqual(42d, restored.EditorLayout.Groups[1].Views[0].VerticalOffset);
    }
}
