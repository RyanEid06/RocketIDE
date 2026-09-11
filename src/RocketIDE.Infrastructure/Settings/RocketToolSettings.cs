namespace RocketIDE.Infrastructure.Settings;

public sealed record RocketToolSettings(
    string? CompilerPath,
    string? LanguageServerPath,
    string[]? TrustedCheckoutRoots = null)
{
    public static RocketToolSettings Automatic { get; } = new(null, null, []);

    public bool IsAutomatic =>
        string.IsNullOrWhiteSpace(CompilerPath) &&
        string.IsNullOrWhiteSpace(LanguageServerPath) &&
        (TrustedCheckoutRoots is null || TrustedCheckoutRoots.Length == 0);
}
