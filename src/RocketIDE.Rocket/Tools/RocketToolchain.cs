namespace RocketIDE.Rocket.Tools;

public sealed record RocketToolchain(
    string CompilerPath,
    string LanguageServerPath,
    string CompilerVersion,
    string LanguageServerVersion);

public sealed record RocketToolDiscoveryResult(
    string? CompilerPath,
    string? LanguageServerPath,
    string? CompilerVersion,
    string? LanguageServerVersion,
    IReadOnlyList<string> Problems)
{
    public RocketToolchain? Toolchain =>
        CompilerPath is not null && LanguageServerPath is not null &&
        CompilerVersion is not null && LanguageServerVersion is not null
            ? new RocketToolchain(CompilerPath, LanguageServerPath, CompilerVersion, LanguageServerVersion)
            : null;
}

public sealed record RocketEnvironmentValidationResult(
    RocketToolchain? Toolchain,
    IReadOnlyList<string> Problems)
{
    public bool IsValid => Toolchain is not null && Problems.Count == 0;
}
