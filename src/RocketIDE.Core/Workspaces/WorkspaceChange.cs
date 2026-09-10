namespace RocketIDE.Core.Workspaces;

public enum WorkspaceChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed,
}

public sealed record WorkspaceChange(WorkspaceChangeKind Kind, string Path, string? OldPath = null);
