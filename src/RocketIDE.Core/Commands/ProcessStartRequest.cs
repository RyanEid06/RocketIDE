namespace RocketIDE.Core.Commands;

public sealed record ProcessStartRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null);
