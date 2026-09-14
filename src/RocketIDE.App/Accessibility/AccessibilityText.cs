namespace RocketIDE.App.Accessibility;

public static class AccessibilityText
{
    public const string RefreshWorkspace = "Refresh workspace";
    public const string CloseDocument = "Close document";
    public const string OpenFile = "Open file";
    public const string OpenFolder = "Open folder";
    public const string SaveFile = "Save active file";
    public const string StopRocket = "Stop Rocket compiler or program";

    public static string ForIcon(string action) => $"{action} button";
}
