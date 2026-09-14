namespace RocketIDE.Core.Recovery;

public interface IRecoveryStore
{
    Task<RecoverySet> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(RecoverySet set, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
