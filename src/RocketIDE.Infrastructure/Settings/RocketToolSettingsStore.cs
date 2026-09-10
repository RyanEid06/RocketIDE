using System.Text.Json;

namespace RocketIDE.Infrastructure.Settings;

public sealed class RocketToolSettingsStore
{
    private readonly string _settingsPath;

    public RocketToolSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public static RocketToolSettingsStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new RocketToolSettingsStore(Path.Combine(localAppData, "RocketIDE", "rocket-tools.json"));
    }

    public async Task<RocketToolSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return RocketToolSettings.Automatic;
        }

        try
        {
            await using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<RocketToolSettings>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? RocketToolSettings.Automatic;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return RocketToolSettings.Automatic;
        }
    }

    public async Task SaveAsync(RocketToolSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings == RocketToolSettings.Automatic)
        {
            await ResetAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new IOException($"Cannot determine settings directory for '{_settingsPath}'.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public Task ResetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            File.Delete(_settingsPath);
        }
        catch (DirectoryNotFoundException)
        {
        }

        return Task.CompletedTask;
    }
}
