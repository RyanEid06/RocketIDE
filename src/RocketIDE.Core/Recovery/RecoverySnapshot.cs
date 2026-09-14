namespace RocketIDE.Core.Recovery;

public sealed record RecoverySnapshot(
    string OriginalPath,
    string? SavedFileFingerprint,
    DateTimeOffset SavedFileLastWriteUtc,
    int BufferVersion,
    string Text,
    DateTimeOffset CapturedUtc,
    string? CurrentDiskFingerprint = null,
    bool HasDiskConflict = false,
    string? ConflictMessage = null)
{
    public bool IsConflict => HasDiskConflict;
}

public sealed class RecoverySet
{
    public RecoverySet()
    {
    }

    public RecoverySet(IEnumerable<RecoverySnapshot> snapshots, DateTimeOffset capturedUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        Snapshots = snapshots.ToList();
        CapturedUtc = capturedUtc;
    }

    public List<RecoverySnapshot> Snapshots { get; set; } = [];

    public DateTimeOffset CapturedUtc { get; set; }

    public static RecoverySet Empty { get; } = new([], DateTimeOffset.UnixEpoch);
    public bool HasSnapshots => Snapshots is { Count: > 0 };
}
