using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RocketIDE.App.Interop;

internal static class WindowsTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    public static void ApplyDarkMode(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var enabled = 1;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
    }

#pragma warning disable SYSLIB1054 // DWM exposes a tiny blittable Win32 API; source-generated marshalling adds no value here.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
#pragma warning restore SYSLIB1054
}
