namespace RocketIDE.Core.Recovery;

public interface ISessionStore
{
    Task<SessionState> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(SessionState state, CancellationToken cancellationToken);
}
