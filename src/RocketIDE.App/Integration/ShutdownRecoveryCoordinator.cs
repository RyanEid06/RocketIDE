using RocketIDE.Core.Recovery;

namespace RocketIDE.App.Integration;

/// <summary>Serializes recovery writes and retains the exit checkpoint until requested saves succeed.</summary>
public sealed class ShutdownRecoveryCoordinator(IRecoveryStore store)
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private bool _stopping;
    private bool _checkpointed;

    public async Task CheckpointAsync(IReadOnlyList<RecoverySnapshot> snapshots, CancellationToken cancellationToken)
    {
        await _writes.WaitAsync(cancellationToken);
        try
        {
            if (_stopping) return;
            if (snapshots.Count == 0) await store.ClearAsync(cancellationToken);
            else await store.SaveAsync(new RecoverySet(snapshots, DateTimeOffset.UtcNow), cancellationToken);
        }
        finally { _writes.Release(); }
    }

    public async Task BeginShutdownAsync(IReadOnlyList<RecoverySnapshot> snapshots, bool preserveExisting, CancellationToken cancellationToken)
    {
        _stopping = true;
        await _writes.WaitAsync(cancellationToken);
        try
        {
            var retained = preserveExisting
                ? (await store.LoadAsync(cancellationToken)).Snapshots.ToDictionary(s => s.OriginalPath, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, RecoverySnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in snapshots) retained[snapshot.OriginalPath] = snapshot;
            if (retained.Count > 0)
                await store.SaveAsync(new RecoverySet(retained.Values, DateTimeOffset.UtcNow), cancellationToken);
            _checkpointed = true;
        }
        finally { _writes.Release(); }
    }

    public async Task CompleteShutdownAsync(bool savesSucceeded, bool preserveExisting, CancellationToken cancellationToken)
    {
        await _writes.WaitAsync(cancellationToken);
        try
        {
            if (_checkpointed && savesSucceeded && !preserveExisting)
                await store.ClearAsync(cancellationToken);
        }
        finally { _writes.Release(); }
    }
}
