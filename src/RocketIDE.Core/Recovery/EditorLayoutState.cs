namespace RocketIDE.Core.Recovery;

public sealed class EditorLayoutState
{
    public string SplitOrientation { get; set; } = "None";
    public List<EditorGroupState> Groups { get; set; } = [];
    public string? ActiveGroupId { get; set; }
    public string? ActiveViewId { get; set; }
}

public sealed class EditorGroupState
{
    public string GroupId { get; set; } = string.Empty;
    public List<EditorViewState> Views { get; set; } = [];
    public string? SelectedViewId { get; set; }
}

public sealed class EditorViewState
{
    public string ViewId { get; set; } = string.Empty;
    public string DocumentPath { get; set; } = string.Empty;
    public int CaretOffset { get; set; }
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public double HorizontalOffset { get; set; }
    public double VerticalOffset { get; set; }
    public List<EditorFoldState> CollapsedFolds { get; set; } = [];
}

public sealed record EditorFoldState(int StartLine, int StartCharacter, int EndLine, int EndCharacter);
