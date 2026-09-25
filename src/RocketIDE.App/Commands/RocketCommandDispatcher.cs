namespace RocketIDE.App.Commands;

public interface IRocketCommandDispatcher
{
    bool IsRegistered(string commandId);
    RocketCommandState GetState(string commandId);
    Task<bool> ExecuteAsync(string commandId, CancellationToken cancellationToken = default);
}

public sealed class RocketCommandDispatcher : IRocketCommandDispatcher
{
    private readonly RocketCommandRegistry _registry;
    private readonly Func<string, RocketCommandState> _stateProvider;
    private readonly Dictionary<string, Func<CancellationToken, Task>> _handlers = new(StringComparer.Ordinal);

    public RocketCommandDispatcher(RocketCommandRegistry registry, Func<string, RocketCommandState> stateProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
    }

    public void Register(string commandId, Func<CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(handler);
        _ = _registry.Get(commandId);
        if (!_handlers.TryAdd(commandId, handler))
        {
            throw new InvalidOperationException($"Command '{commandId}' is already registered.");
        }
    }

    public bool IsRegistered(string commandId) => _handlers.ContainsKey(commandId);

    public RocketCommandState GetState(string commandId) => _stateProvider(commandId);

    public async Task<bool> ExecuteAsync(string commandId, CancellationToken cancellationToken = default)
    {
        _ = _registry.Get(commandId);
        if (!_handlers.TryGetValue(commandId, out var handler))
        {
            return false;
        }
        if (!GetState(commandId).IsEnabled)
        {
            return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        await handler(cancellationToken).ConfigureAwait(true);
        return true;
    }
}
