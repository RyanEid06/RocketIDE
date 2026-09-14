namespace RocketIDE.Rocket.Compiler;

public sealed record RocketMessageSpan(string? File, int? Line, int? Column);

public sealed record RocketMessage(
    string Reason,
    string? Level = null,
    string? Code = null,
    string? Message = null,
    RocketMessageSpan? Span = null,
    string? Command = null,
    bool? Success = null,
    string? Artifact = null,
    string? Cache = null,
    string? Name = null,
    string? Status = null,
    int? ExitCode = null,
    int? Passed = null,
    int? Failed = null,
    int? ExpectedFailures = null,
    int? Selected = null);
