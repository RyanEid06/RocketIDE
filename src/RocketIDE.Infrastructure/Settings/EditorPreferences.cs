namespace RocketIDE.Infrastructure.Settings;

public sealed record EditorPreferences
{
    public const double DefaultFontSize = 14.0;
    public const double MinimumFontSize = 8.0;
    public const double MaximumFontSize = 32.0;

    public double FontSize { get; init; } = DefaultFontSize;
    public bool WordWrap { get; init; }
    public bool FormatOnSave { get; init; }

    public static EditorPreferences Default { get; } = new();

    public EditorPreferences Normalize()
    {
        var fontSize = double.IsFinite(FontSize)
            ? Math.Clamp(FontSize, MinimumFontSize, MaximumFontSize)
            : DefaultFontSize;
        return this with { FontSize = fontSize };
    }
}
