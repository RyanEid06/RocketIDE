namespace RocketIDE.Core.Recovery;

public sealed record WindowBounds(double Left, double Top, double Width, double Height, bool IsMaximized = false)
{
    public static WindowBounds Default { get; } = new(100, 100, 1280, 800);

    public WindowBounds ClampToWorkArea(double workAreaLeft, double workAreaTop, double workAreaWidth, double workAreaHeight)
    {
        if (workAreaWidth <= 0 || workAreaHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workAreaWidth));
        }

        var width = Math.Clamp(Width, 320, workAreaWidth);
        var height = Math.Clamp(Height, 240, workAreaHeight);
        var left = Math.Clamp(Left, workAreaLeft, workAreaLeft + workAreaWidth - width);
        var top = Math.Clamp(Top, workAreaTop, workAreaTop + workAreaHeight - height);
        return new WindowBounds(left, top, width, height, IsMaximized);
    }
}
