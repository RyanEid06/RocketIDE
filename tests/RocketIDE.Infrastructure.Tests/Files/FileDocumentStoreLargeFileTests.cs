using RocketIDE.Infrastructure.Files;

namespace RocketIDE.Infrastructure.Tests.Files;

[TestClass]
public sealed class FileDocumentStoreLargeFileTests
{
    [TestMethod]
    public async Task OpenAsync_RejectsBuffersAboveLocalEditorSafetyLimitBeforeReadingAllBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "RocketIDE.Tests", $"large-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.rocket");
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                stream.SetLength(64L * 1024 * 1024 + 1);
            }

            var store = new FileDocumentStore();

            await Assert.ThrowsExactlyAsync<UnsupportedTextFileException>(() => store.OpenAsync(path, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
