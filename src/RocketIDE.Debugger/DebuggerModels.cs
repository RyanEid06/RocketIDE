namespace RocketIDE.Debugger;

public enum RocketDebugSessionState
{
    Idle,
    Launching,
    Running,
    Stopped,
    Terminated,
    Faulted,
}

public sealed record RocketDebugBreakpoint
{
    public RocketDebugBreakpoint(string sourcePath, int line, bool isBound = false, string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!Path.IsPathFullyQualified(sourcePath))
        {
            throw new ArgumentException("Debugger source paths must be absolute.", nameof(sourcePath));
        }
        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        SourcePath = Path.GetFullPath(sourcePath);
        Line = line;
        IsBound = isBound;
        Message = message;
    }

    public string SourcePath { get; init; }
    public int Line { get; init; }
    public bool IsBound { get; init; }
    public string? Message { get; init; }
}

public sealed record RocketDebugThread(int Index, int SystemId, string DisplayName, bool IsCurrent);

public sealed record RocketDebugStackFrame(
    int Index,
    string FunctionName,
    ulong? InstructionPointer,
    string? SourcePath,
    int? Line);

public sealed record RocketDebugVariable(string Name, string? Type, string Value);

public sealed record RocketDebugStopLocation(string SourcePath, int Line, string Reason);

public sealed record RocketDebugLaunchRequest
{
    public RocketDebugLaunchRequest(
        string executablePath,
        string pdbPath,
        string sourceMapPath,
        string sourceRoot,
        string workingDirectory,
        IReadOnlyList<string>? arguments,
        IReadOnlyList<RocketDebugBreakpoint>? breakpoints)
    {
        ExecutablePath = NormalizeFilePath(executablePath, nameof(executablePath));
        PdbPath = NormalizeFilePath(pdbPath, nameof(pdbPath));
        SourceMapPath = NormalizeFilePath(sourceMapPath, nameof(sourceMapPath));
        SourceRoot = NormalizeDirectoryPath(sourceRoot, nameof(sourceRoot));
        WorkingDirectory = NormalizeDirectoryPath(workingDirectory, nameof(workingDirectory));
        Arguments = arguments?.ToArray() ?? [];
        Breakpoints = breakpoints?.ToArray() ?? [];
    }

    public string ExecutablePath { get; }
    public string PdbPath { get; }
    public string SourceMapPath { get; }
    public string SourceRoot { get; }
    public string WorkingDirectory { get; }
    public IReadOnlyList<string> Arguments { get; }
    public IReadOnlyList<RocketDebugBreakpoint> Breakpoints { get; }

    private static string NormalizeFilePath(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return Path.GetFullPath(value);
    }

    private static string NormalizeDirectoryPath(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return Path.GetFullPath(value);
    }
}

public sealed class RocketDebugStateChangedEventArgs(RocketDebugSessionState state, string? message = null) : EventArgs
{
    public RocketDebugSessionState State { get; } = state;
    public string? Message { get; } = message;
}

public sealed class RocketDebugOutputEventArgs(string text) : EventArgs
{
    public string Text { get; } = text ?? string.Empty;
}

public sealed class RocketDebugStoppedEventArgs(RocketDebugStopLocation? location) : EventArgs
{
    public RocketDebugStopLocation? Location { get; } = location;
}
