namespace RocketIDE.Core.Workspaces;

public sealed record WorkspaceRoot(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
}
