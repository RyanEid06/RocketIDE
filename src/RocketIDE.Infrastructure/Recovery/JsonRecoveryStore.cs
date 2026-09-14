using System.Security.Cryptography;
using System.Text.Json;
using RocketIDE.Core.Recovery;

namespace RocketIDE.Infrastructure.Recovery;

public sealed class JsonRecoveryStore : IRecoveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public JsonRecoveryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public static JsonRecoveryStore CreateDefault()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new JsonRecoveryStore(Path.Combine(root, "RocketIDE", "recovery.json"));
    }

    public async Task<RecoverySet> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return RecoverySet.Empty;
        }

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var set = await JsonSerializer.DeserializeAsync<RecoverySet>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? RecoverySet.Empty;
            return await AddDiskConflictStateAsync(set, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return RecoverySet.Empty;
        }
    }

    public Task SaveAsync(RecoverySet set, CancellationToken cancellationToken) =>
        JsonSessionStore.AtomicJsonWriteAsync(_path, set, cancellationToken);

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (FileNotFoundException)
        {
        }
    }

    public static Task<RecoverySnapshot> CreateSnapshotAsync(
        string originalPath,
        string? savedFileFingerprint,
        DateTimeOffset savedFileLastWriteUtc,
        int bufferVersion,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentNullException.ThrowIfNull(text);
        return Task.FromResult(new RecoverySnapshot(Path.GetFullPath(originalPath), savedFileFingerprint, savedFileLastWriteUtc,
            bufferVersion, text, DateTimeOffset.UtcNow));
    }

    public static async Task<string?> ComputeFingerprintAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static async Task<RecoverySet> AddDiskConflictStateAsync(RecoverySet set, CancellationToken cancellationToken)
    {
        var savedSnapshots = set.Snapshots;
        var snapshots = new List<RecoverySnapshot>(savedSnapshots.Count);
        foreach (var snapshot in savedSnapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await ComputeFingerprintAsync(snapshot.OriginalPath, cancellationToken).ConfigureAwait(false);
            var diskConflict = !string.Equals(current, snapshot.SavedFileFingerprint, StringComparison.OrdinalIgnoreCase);
            var conflict = snapshot.HasDiskConflict || diskConflict;
            var conflictMessage = snapshot.HasDiskConflict && !string.IsNullOrWhiteSpace(snapshot.ConflictMessage)
                ? snapshot.ConflictMessage
                : conflict
                    ? "The file changed, was deleted, or was recreated after this recovery snapshot."
                    : null;
            snapshots.Add(snapshot with
            {
                CurrentDiskFingerprint = current,
                HasDiskConflict = conflict,
                ConflictMessage = conflictMessage,
            });
        }

        return new RecoverySet(snapshots, set.CapturedUtc);
    }
}
