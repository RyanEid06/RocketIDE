using RocketIDE.Core.Logging;
using RocketIDE.Infrastructure.Logging;

namespace RocketIDE.Infrastructure.Tests.Logging;

[TestClass]
public sealed class RotatingFileLoggerTests
{
    [TestMethod]
    public void LoggerRedactsCredentialLikeValues()
    {
        var redacted = RotatingFileLogger.Redact("token=secret-value password: hunter2");

        Assert.IsFalse(redacted.Contains("secret-value", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("hunter2", StringComparison.Ordinal));
        StringAssert.Contains(redacted, "<redacted>");
    }

    [TestMethod]
    public void LoggerRotatesWhenSizeLimitIsReached()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "rocketide.log");
        using var logger = new RotatingFileLogger(path, maxBytes: 40, maxFiles: 2);

        logger.Log(ApplicationLogLevel.Information, "first message that is longer than forty bytes");
        logger.Log(ApplicationLogLevel.Information, "second message that is longer than forty bytes");

        Assert.IsTrue(File.Exists(path));
        Assert.IsTrue(File.Exists(path + ".1"));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rocketide-log-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
