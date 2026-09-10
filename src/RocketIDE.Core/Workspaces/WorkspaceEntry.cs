namespace RocketIDE.Core.Workspaces;

public sealed record WorkspaceEntry(string Path, string Name, bool IsDirectory, bool HasChildren);
