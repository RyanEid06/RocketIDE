using RocketIDE.Rocket.Diagnostics;
using RocketIDE.Rocket.LanguageServer;

namespace RocketIDE.App.Integration;

public sealed class RocketDiagnosticSessionChangedEventArgs(long generation, bool isOnline) : EventArgs
{
    public long Generation { get; } = generation;
    public bool IsOnline { get; } = isOnline;
}

public sealed class RocketDiagnosticsPublishedEventArgs(RocketDiagnosticPublication publication) : EventArgs
{
    public RocketDiagnosticPublication Publication { get; } = publication ?? throw new ArgumentNullException(nameof(publication));
}

public sealed class RocketDocumentSyncStateChangedEventArgs(string path, int version, LspDocumentSyncState state) : EventArgs
{
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));
    public int Version { get; } = version;
    public LspDocumentSyncState State { get; } = state;
}
