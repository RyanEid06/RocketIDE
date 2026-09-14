namespace RocketIDE.Core.Output;

public enum ProcessOutputStream
{
    StandardOutput,
    StandardError,
}

public sealed record ProcessOutput(string Text, ProcessOutputStream Stream);
