namespace RocketIDE.Core.Recovery;

public sealed class SessionState
{
    public SessionState()
    {
    }

    public SessionState(
        string? workspacePath,
        IEnumerable<string> openDocumentPaths,
        string? activeDocumentPath,
        PanelLayout panels,
        WindowBounds window,
        bool cleanShutdown,
        DateTimeOffset savedUtc)
    {
        ArgumentNullException.ThrowIfNull(openDocumentPaths);
        WorkspacePath = workspacePath;
        OpenDocumentPaths = openDocumentPaths.ToList();
        ActiveDocumentPath = activeDocumentPath;
        Panels = panels ?? throw new ArgumentNullException(nameof(panels));
        Window = window ?? throw new ArgumentNullException(nameof(window));
        CleanShutdown = cleanShutdown;
        SavedUtc = savedUtc;
    }

    public string? WorkspacePath { get; set; }
    public List<string> OpenDocumentPaths { get; set; } = [];
    public string? ActiveDocumentPath { get; set; }
    public PanelLayout Panels { get; set; } = new();
    public WindowBounds Window { get; set; } = WindowBounds.Default;
    public EditorLayoutState? EditorLayout { get; set; }
    public bool CleanShutdown { get; set; } = true;
    public DateTimeOffset SavedUtc { get; set; } = DateTimeOffset.UnixEpoch;

    public static SessionState Empty { get; } = new();
}
