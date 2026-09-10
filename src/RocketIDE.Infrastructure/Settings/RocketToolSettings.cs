namespace RocketIDE.Infrastructure.Settings;

public sealed record RocketToolSettings(string? CompilerPath, string? LanguageServerPath)
{
    public static RocketToolSettings Automatic { get; } = new(null, null);
}
