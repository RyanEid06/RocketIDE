namespace RocketIDE.Rocket.Tools;

public sealed record RocketToolDiscoveryOptions(
    string? CompilerPath,
    string? LanguageServerPath,
    string InstallationDirectory,
    IReadOnlyCollection<string>? TrustedCheckoutRoots = null);
