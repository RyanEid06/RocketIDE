namespace RocketIDE.App.Commands;

public sealed record RocketCommandContext(bool HasDocument, bool HasTarget, bool CanRun, bool IsBusy);

public sealed record RocketCommandDefinition(
    string Id,
    string DisplayName,
    string Gesture,
    Func<RocketCommandContext, bool> CanExecute);

public sealed record RocketCommandState(string Id, bool IsEnabled, bool IsBusy);

public sealed class RocketCommandRegistry
{
    public const string Build = "rocket.build";
    public const string Run = "rocket.run";
    public const string Stop = "rocket.stop";
    public const string Test = "rocket.test";
    public const string Search = "workspace.search";
    public const string Replace = "workspace.replace";

    private readonly IReadOnlyList<RocketCommandDefinition> _definitions =
    [
        new(Build, "Build Rocket target", "Ctrl+B", context => context.HasTarget && !context.IsBusy),
        new(Run, "Run Rocket target", "Ctrl+R", context => context.HasTarget && context.CanRun && !context.IsBusy),
        new(Stop, "Stop Rocket", "", context => context.IsBusy),
        new(Test, "Test Rocket target", "", context => context.HasTarget && !context.IsBusy),
        new(Search, "Search in workspace", "Ctrl+Shift+F", _ => true),
        new(Replace, "Replace in files", "Ctrl+Shift+H", _ => true),
    ];

    public IReadOnlyList<RocketCommandDefinition> Definitions => _definitions;

    public RocketCommandDefinition Get(string id) =>
        _definitions.FirstOrDefault(definition => string.Equals(definition.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown RocketIDE command '{id}'.");

    public RocketCommandState Evaluate(string id, RocketCommandContext context)
    {
        var definition = Get(id);
        return new RocketCommandState(id, definition.CanExecute(context), context.IsBusy);
    }
}
