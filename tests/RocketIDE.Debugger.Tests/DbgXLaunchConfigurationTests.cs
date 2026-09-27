using System.Reflection;
using DbgX.Interfaces.Services;
using RocketIDE.Debugger;

namespace RocketIDE.Debugger.Tests;

[TestClass]
public sealed class DbgXLaunchConfigurationTests
{
    [TestMethod]
    public void LaunchSearchesOnlyTargetSymbolsWithoutNetworkFallback()
    {
        var options = DbgXCommandTransport.CreateLaunchOptions(@"C:\build\app.exe", @"C:\project");
        Assert.AreEqual(@"C:\build", options.SymPath);
        Assert.AreEqual(true, options.SymOptIgnoreNtSympath);
        Assert.AreEqual(@"C:\project", options.StartDirectory);
    }

    [TestMethod]
    public void LaunchSetsTheRequestedWorkingDirectoryOnTheInstalledDbgXOptions()
    {
        var options = new EngineOptions();
        var directory = Path.GetFullPath(Path.GetTempPath());
        var method = typeof(DbgXCommandTransport).GetMethod("TrySetWorkingDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;

        method.Invoke(null, [options, directory]);

        Assert.AreEqual(directory, options.StartDirectory);
    }
}
