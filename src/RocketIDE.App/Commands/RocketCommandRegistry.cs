namespace RocketIDE.App.Commands;

public sealed record RocketCommandContext(
    bool HasDocument,
    bool HasTarget,
    bool CanRun,
    bool IsBusy,
    bool HasWorkspace = false);

public sealed record RocketCommandDefinition(
    string Id,
    string DisplayName,
    string Gesture,
    Func<RocketCommandContext, bool> CanExecute);

public sealed record RocketCommandState(string Id, bool IsEnabled, bool IsBusy);

public sealed class RocketCommandRegistry
{
    public const string Check = "rocket.check";
    public const string Build = "rocket.build";
    public const string Run = "rocket.run";
    public const string Stop = "rocket.stop";
    public const string Test = "rocket.test";
    public const string NewProject = "rocket.new";
    public const string Resolve = "rocket.resolve";
    public const string DependencyTree = "rocket.tree";
    public const string Audit = "rocket.audit";
    public const string TargetInfo = "rocket.target";
    public const string FormatTarget = "rocket.format";
    public const string Coverage = "rocket.coverage";
    public const string Profile = "rocket.profile";
    public const string Benchmark = "rocket.benchmark";
    public const string Search = "workspace.search";
    public const string Replace = "workspace.replace";
    public const string QuickOpen = "workspace.quickOpen";
    public const string Problems = "view.problems";
    public const string Output = "view.output";

    private readonly IReadOnlyList<RocketCommandDefinition> _definitions =
    [
        new(Check, "Check Rocket target", "", TargetReady),
        new(Build, "Build Rocket target", "Ctrl+B", TargetReady),
        new(Run, "Run Rocket target", "Ctrl+R", context => context.HasTarget && context.CanRun && !context.IsBusy),
        new(Stop, "Stop Rocket", "Ctrl+Shift+F5", context => context.IsBusy),
        new(Test, "Test Rocket target", "", TargetReady),
        new(NewProject, "New Rocket project", "", context => context.HasWorkspace && !context.IsBusy),
        new(Resolve, "Resolve dependencies", "", TargetReady),
        new(DependencyTree, "Dependency tree", "", TargetReady),
        new(Audit, "Audit dependencies", "", TargetReady),
        new(TargetInfo, "Target information", "", TargetReady),
        new(FormatTarget, "Format target/workspace", "", TargetReady),
        new(Coverage, "Coverage", "", TargetReady),
        new(Profile, "Profile", "", TargetReady),
        new(Benchmark, "Benchmark", "", TargetReady),
        new(Search, "Search in workspace", "Ctrl+Shift+F", _ => true),
        new(Replace, "Replace in files", "Ctrl+Shift+H", _ => true),
        new(QuickOpen, "Quick open file", "Ctrl+P", context => context.HasWorkspace),
        new(Problems, "Show Problems", "Ctrl+Shift+M", _ => true),
        new(Output, "Show Output", "Ctrl+Shift+U", _ => true),
    ];

    public IReadOnlyList<RocketCommandDefinition> Definitions => _definitions;

    public RocketCommandDefinition Get(string id) =>
        _definitions.FirstOrDefault(definition => string.Equals(definition.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown RocketIDE command '{id}'.");

    public RocketCommandDefinition? FindByGesture(string gesture) =>
        _definitions.FirstOrDefault(definition =>
            !string.IsNullOrWhiteSpace(definition.Gesture) &&
            string.Equals(definition.Gesture, gesture, StringComparison.OrdinalIgnoreCase));

    public RocketCommandState Evaluate(string id, RocketCommandContext context)
    {
        var definition = Get(id);
        return new RocketCommandState(id, definition.CanExecute(context), context.IsBusy);
    }

    private static bool TargetReady(RocketCommandContext context) => context.HasTarget && !context.IsBusy;
}
