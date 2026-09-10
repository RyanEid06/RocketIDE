using RocketIDE.Rocket.Projects;

namespace RocketIDE.Rocket.Tests.Projects;

[TestClass]
public sealed class RocketManifestTests
{
    [TestMethod]
    public void Read_ParsesOnlyPresentationMetadataAndPreservesHashInsideQuotes()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                [package]
                name = "demo#name" # trailing comment
                entry = "app/start.rocket"

                [build]
                kind = "library"
                name = "demo_lib"

                [dependencies]
                ignored = "do-not-resolve"
                """);

            var manifest = RocketManifest.Read(path);

            Assert.AreEqual("demo#name", manifest.PackageName);
            Assert.AreEqual(Path.Combine("app", "start.rocket"), manifest.Entry);
            Assert.AreEqual("library", manifest.OutputKind);
            Assert.AreEqual("demo_lib", manifest.OutputName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Read_UsesRocketDefaultsWhenMetadataIsMissing()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[dependencies]\nfoo = \"1.0\"\n");

            var manifest = RocketManifest.Read(path);

            Assert.IsNull(manifest.PackageName);
            Assert.AreEqual(Path.Combine("src", "main.rocket"), manifest.Entry);
            Assert.AreEqual("executable", manifest.OutputKind);
            Assert.AreEqual("main", manifest.OutputName);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
