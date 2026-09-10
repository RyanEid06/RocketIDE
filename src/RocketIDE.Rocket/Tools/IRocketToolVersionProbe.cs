namespace RocketIDE.Rocket.Tools;

public interface IRocketToolVersionProbe
{
    Task<string> GetVersionAsync(string executablePath, CancellationToken cancellationToken);
}
