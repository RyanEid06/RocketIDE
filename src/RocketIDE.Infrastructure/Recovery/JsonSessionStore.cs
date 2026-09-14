using System.Text.Json;
using RocketIDE.Core.Recovery;

namespace RocketIDE.Infrastructure.Recovery;

public sealed class JsonSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public JsonSessionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public static JsonSessionStore CreateDefault()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new JsonSessionStore(Path.Combine(root, "RocketIDE", "session.json"));
    }

    public async Task<SessionState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return SessionState.Empty;
        }

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<SessionState>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? SessionState.Empty;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return SessionState.Empty;
        }
    }

    public async Task SaveAsync(SessionState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await AtomicJsonWriteAsync(_path, state, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task AtomicJsonWriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new IOException($"Cannot determine the settings directory for '{path}'.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
