namespace RocketIDE.App.Commands;

public sealed record RocketCommandContext(
    bool HasDocument,
    bool HasTarget,
    bool CanRun,
    bool IsBusy,
    bool HasWorkspace = false,
    bool IsDebugging = false,
    bool IsDebuggerRunning = false,
    bool IsDebuggerStopped = false,
    bool HasRocketDocument = false);

public sealed record RocketCommandDefinition(
    string Id,
    string DisplayName,
    string Gesture,
    Func<RocketCommandContext, bool> CanExecute,
    string Category = "General");

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
    public const string CommandPalette = "view.commandPalette";
    public const string Outline = "view.outline";
    public const string Undo = "editor.undo";
    public const string Redo = "editor.redo";
    public const string SelectAll = "editor.selectAll";
    public const string Find = "editor.find";
    public const string ReplaceDocument = "editor.replace";
    public const string GoToLine = "navigate.goToLine";
    public const string GoToDefinition = "navigate.definition";
    public const string FindReferences = "navigate.references";
    public const string RenameSymbol = "editor.renameSymbol";
    public const string CodeActions = "editor.codeActions";
    public const string FormatDocument = "editor.formatDocument";
    public const string GoToSymbolFile = "navigate.symbolFile";
    public const string GoToSymbolWorkspace = "navigate.symbolWorkspace";
    public const string NavigateBack = "navigate.back";
    public const string NavigateForward = "navigate.forward";
    public const string DebugStartContinue = "debug.startContinue";
    public const string DebugPause = "debug.pause";
    public const string DebugStop = "debug.stop";
    public const string DebugToggleBreakpoint = "debug.toggleBreakpoint";
    public const string DebugStepOver = "debug.stepOver";
    public const string DebugStepInto = "debug.stepInto";
    public const string DebugStepOut = "debug.stepOut";

    private readonly IReadOnlyList<RocketCommandDefinition> _definitions =
    [
        new(CommandPalette, "Show Command Palette", "Ctrl+Shift+P", _ => true, "View"),
        new(Outline, "Show Outline", "", context => context.HasRocketDocument, "View"),
        new(Undo, "Undo", "Ctrl+Z", context => context.HasDocument, "Editor"),
        new(Redo, "Redo", "Ctrl+Y", context => context.HasDocument, "Editor"),
        new(SelectAll, "Select All", "Ctrl+A", context => context.HasDocument, "Editor"),
        new(Find, "Find in Document", "Ctrl+F", context => context.HasDocument, "Editor"),
        new(ReplaceDocument, "Replace in Document", "Ctrl+H", context => context.HasDocument, "Editor"),
        new(GoToLine, "Go to Line", "Ctrl+G", context => context.HasDocument, "Navigate"),
        new(GoToDefinition, "Go to Definition", "F12", context => context.HasRocketDocument, "Navigate"),
        new(FindReferences, "Find References", "Shift+F12", context => context.HasRocketDocument, "Navigate"),
        new(RenameSymbol, "Rename Symbol", "F2", context => context.HasRocketDocument, "Editor"),
        new(CodeActions, "Quick Fixes (server-provided)", "Ctrl+.", context => context.HasRocketDocument, "Editor"),
        new(FormatDocument, "Format Document", "Shift+Alt+F", context => context.HasRocketDocument, "Editor"),
        new(GoToSymbolFile, "Go to Symbol in File", "", context => context.HasRocketDocument, "Navigate"),
        new(GoToSymbolWorkspace, "Go to Symbol in Workspace", "Ctrl+T", context => context.HasWorkspace, "Navigate"),
        new(NavigateBack, "Navigate Back", "Alt+Left", _ => true, "Navigate"),
        new(NavigateForward, "Navigate Forward", "Alt+Right", _ => true, "Navigate"),
        new(Check, "Check Rocket target", "", TargetReady, "Build"),
        new(Build, "Build Rocket target", "Ctrl+B", TargetReady, "Build"),
        new(Run, "Run Rocket target", "Ctrl+R", context => context.HasTarget && context.CanRun && !context.IsBusy && !context.IsDebugging, "Run"),
        new(Stop, "Stop Rocket", "Ctrl+Shift+F5", context => context.IsBusy, "Run"),
        new(Test, "Test Rocket target", "", TargetReady, "Run"),
        new(NewProject, "New Rocket project", "", context => context.HasWorkspace && !context.IsBusy && !context.IsDebugging, "Workspace"),
        new(Resolve, "Resolve dependencies", "", TargetReady, "Tools"),
        new(DependencyTree, "Dependency tree", "", TargetReady, "Tools"),
        new(Audit, "Audit dependencies", "", TargetReady, "Tools"),
        new(TargetInfo, "Target information", "", TargetReady, "Tools"),
        new(FormatTarget, "Format target/workspace", "", TargetReady, "Tools"),
        new(Coverage, "Coverage", "", TargetReady, "Tools"),
        new(Profile, "Profile", "", TargetReady, "Tools"),
        new(Benchmark, "Benchmark", "", TargetReady, "Tools"),
        new(Search, "Search in workspace", "Ctrl+Shift+F", _ => true, "Workspace"),
        new(Replace, "Replace in files", "Ctrl+Shift+H", _ => true, "Workspace"),
        new(QuickOpen, "Quick open file", "Ctrl+P", context => context.HasWorkspace, "Workspace"),
        new(Problems, "Show Problems", "Ctrl+Shift+M", _ => true, "View"),
        new(Output, "Show Output", "Ctrl+Shift+U", _ => true, "View"),
        new(DebugStartContinue, "Start/Continue Debugging", "F5", context =>
            !context.IsBusy && context.HasTarget && context.CanRun && (!context.IsDebugging || context.IsDebuggerStopped), "Debug"),
        new(DebugPause, "Pause Debugging", "Pause", context => context.IsDebuggerRunning, "Debug"),
        new(DebugStop, "Stop Debugging", "Shift+F5", context => context.IsDebugging, "Debug"),
        new(DebugToggleBreakpoint, "Toggle Breakpoint", "F9", context => context.HasRocketDocument && !context.IsDebuggerRunning, "Debug"),
        new(DebugStepOver, "Step Over", "F10", context => context.IsDebuggerStopped, "Debug"),
        new(DebugStepInto, "Step Into", "F11", context => context.IsDebuggerStopped, "Debug"),
        new(DebugStepOut, "Step Out", "Shift+F11", context => context.IsDebuggerStopped, "Debug"),
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

    private static bool TargetReady(RocketCommandContext context) => context.HasTarget && !context.IsBusy && !context.IsDebugging;
}
