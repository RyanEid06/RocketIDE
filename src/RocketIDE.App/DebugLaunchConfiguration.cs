using System.IO;
using RocketIDE.Infrastructure.Settings;

namespace RocketIDE.App;

internal sealed class DebugLaunchConfiguration(string inputPath, RocketToolSettings settings)
{
    public string InputPath { get; } = Path.GetFullPath(inputPath);
    public RocketToolSettings Settings { get; } = settings with { TrustedCheckoutRoots = settings.TrustedCheckoutRoots?.ToArray() };
}
