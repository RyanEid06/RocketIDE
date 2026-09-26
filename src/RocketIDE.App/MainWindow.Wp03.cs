using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RocketIDE.App.Commands;
using RocketIDE.App.Editor;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.App.Views;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.Compiler;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App;

public partial class MainWindow
{
    private RocketCommandDispatcher _wp03CommandDispatcher = null!;
    private DocumentSymbolService _wp03DocumentSymbols = null!;
    private RocketFoldingRangeProvider _wp03FoldingProvider = null!;
    private NavigationHistoryService _wp03NavigationHistory = null!;
    private OutlineViewModel _wp03Outline = null!;
    private OutlineWindow? _wp03OutlineWindow;
    private CancellationTokenSource? _wp03ProjectStatusCancellation;
    private long _wp03UnsupportedProjectStatusGeneration;

    public IRocketFoldingRangeProvider RocketFoldingRanges => _wp03FoldingProvider;

    private void InitializeWp03()
    {
        _wp03DocumentSymbols = new DocumentSymbolService(
            _rocketSession.RequestDocumentSymbolsAsync,
            () => _rocketSession.SessionGeneration);
        _wp03FoldingProvider = new RocketFoldingRangeProvider(
            _rocketSession.RequestFoldingRangesAsync,
            () => _rocketSession.SessionGeneration,
            _lifetime.WorkToken);
        var validatedNavigation = new ValidatedEditorNavigation(
            EditorNavigation,
            async (path, token) => FindOpenDocument(path)?.Text ?? await ReadNavigationTextAsync(path, token),
            message => AppendRocketOutput(message));
        _wp03NavigationHistory = new NavigationHistoryService(EditorContext, validatedNavigation, 100);
        _wp03Outline = new OutlineViewModel(EditorContext, _wp03DocumentSymbols, _wp03NavigationHistory, _lifetime.WorkToken);
        _wp03CommandDispatcher = new RocketCommandDispatcher(_viewModel.CommandRegistry, GetWp03CommandState);
        RegisterWp03Commands();
        _wp03NavigationHistory.Changed += Wp03NavigationHistory_Changed;
        _rocketSession.DiagnosticSessionChanged += Wp03DiagnosticSessionChanged;
        _rocketSession.DocumentSyncStateChanged += Wp03DocumentSyncStateChanged;
        Closed += (_, _) => DisposeWp03();
    }

    private RocketCommandState GetWp03CommandState(string commandId)
    {
        if (string.Equals(commandId, RocketCommandRegistry.NavigateBack, StringComparison.Ordinal))
            return new RocketCommandState(commandId, _wp03NavigationHistory.CanGoBack, false);
        if (string.Equals(commandId, RocketCommandRegistry.NavigateForward, StringComparison.Ordinal))
            return new RocketCommandState(commandId, _wp03NavigationHistory.CanGoForward, false);
        return _viewModel.GetCommandState(commandId);
    }

    private void RegisterWp03Commands()
    {
        void Register(string id, Action action) => _wp03CommandDispatcher.Register(id, _ =>
        {
            action();
            return Task.CompletedTask;
        });

        _wp03CommandDispatcher.Register(RocketCommandRegistry.CommandPalette, ShowCommandPaletteAsync);
        Register(RocketCommandRegistry.Undo, () => EditorContext.ActiveView?.CommandTarget?.Undo());
        Register(RocketCommandRegistry.Redo, () => EditorContext.ActiveView?.CommandTarget?.Redo());
        Register(RocketCommandRegistry.SelectAll, () => EditorContext.ActiveView?.CommandTarget?.SelectAll());
        Register(RocketCommandRegistry.Find, () => EditorContext.ActiveView?.CommandTarget?.ShowFind(includeReplace: false));
        Register(RocketCommandRegistry.ReplaceDocument, () => EditorContext.ActiveView?.CommandTarget?.ShowFind(includeReplace: true));
        Register(RocketCommandRegistry.GoToLine, ShowWp03GotoLine);
        _wp03CommandDispatcher.Register(RocketCommandRegistry.GoToDefinition, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.Definition, token));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.FindReferences, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.References, token));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.RenameSymbol, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.Rename, token));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.CodeActions, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.CodeActions, token));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.FormatDocument, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.FormatDocument, token));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.GoToSymbolFile, ShowFileSymbolsAsync);
        _wp03CommandDispatcher.Register(RocketCommandRegistry.GoToSymbolWorkspace, ShowWorkspaceSymbolsAsync);
        _wp03CommandDispatcher.Register(RocketCommandRegistry.NavigateBack, token => _wp03NavigationHistory.GoBackAsync(token));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.NavigateForward, token => _wp03NavigationHistory.GoForwardAsync(token));
        Register(RocketCommandRegistry.Outline, ShowOutline);

        _wp03CommandDispatcher.Register(RocketCommandRegistry.Check, _ => ExecuteRocketCommandAsync(RocketCommandKind.Check));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.Build, _ => ExecuteRocketCommandAsync(RocketCommandKind.Build));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.Run, _ => ExecuteRocketCommandAsync(RocketCommandKind.Run));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.Test, _ => ExecuteRocketCommandAsync(RocketCommandKind.Test));
        Register(RocketCommandRegistry.Stop, () => StopRocket_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.NewProject, () => NewRocketProject_Click(this, new RoutedEventArgs()));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.Resolve, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Resolve, new RocketAdvancedCommandOptions()));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.DependencyTree, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Tree, new RocketAdvancedCommandOptions()));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.Audit, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Audit, new RocketAdvancedCommandOptions()));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.TargetInfo, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Target, new RocketAdvancedCommandOptions(Verbose: true)));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.FormatTarget, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Format, new RocketAdvancedCommandOptions()));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.Coverage, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Coverage, CreateMeasurementOptions("coverage")));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.Profile, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Profile, CreateMeasurementOptions("profile")));
        _wp03CommandDispatcher.Register(RocketCommandRegistry.Benchmark, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Benchmark, CreateMeasurementOptions("benchmark")));
        Register(RocketCommandRegistry.Search, () => SearchWorkspace_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.Replace, () => ReplaceWorkspace_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.QuickOpen, () => QuickOpenFile_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.Problems, () => ShowProblems_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.Output, () => ShowOutput_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.DebugStartContinue, () => DebugStartContinue_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.DebugPause, () => DebugPause_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.DebugStop, () => DebugStop_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.DebugToggleBreakpoint, () => DebugToggleBreakpoint_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.DebugStepOver, () => DebugStepOver_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.DebugStepInto, () => DebugStepInto_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.DebugStepOut, () => DebugStepOut_Click(this, new RoutedEventArgs()));
    }

    private async void RegisteredCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string commandId } || !_wp03CommandDispatcher.IsRegistered(commandId)) return;
        try
        {
            await _wp03CommandDispatcher.ExecuteAsync(commandId, _lifetime.WorkToken);
        }
        catch (OperationCanceledException) when (_lifetime.IsStopping)
        {
        }
        catch (Exception exception)
        {
            AppendRocketOutput($"Command '{commandId}' failed: {exception.Message}");
            ShowOutputPanel();
        }
    }

    private static bool ShouldPreserveFocusedTextInputGesture(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase) return false;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return false;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key is Key.A or Key.Z or Key.Y;
    }

    private async Task<bool> TryDispatchWp03GestureAsync(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var gesture = FormatWp03Gesture(key, Keyboard.Modifiers);
        if (string.IsNullOrEmpty(gesture)) return false;
        var definition = _viewModel.CommandRegistry.FindByGesture(gesture);
        if (definition is null || !_wp03CommandDispatcher.IsRegistered(definition.Id)) return false;
        e.Handled = true;
        try
        {
            await _wp03CommandDispatcher.ExecuteAsync(definition.Id, _lifetime.WorkToken);
        }
        catch (OperationCanceledException) when (_lifetime.IsStopping)
        {
        }
        catch (Exception exception)
        {
            AppendRocketOutput($"Command '{definition.Id}' failed: {exception.Message}");
            ShowOutputPanel();
        }
        return true;
    }

    private static string FormatWp03Gesture(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Windows");
        var keyText = key == Key.OemPeriod ? "." : key.ToString();
        if (string.IsNullOrWhiteSpace(keyText) || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)
            return string.Empty;
        parts.Add(keyText);
        return string.Join("+", parts);
    }

    private async Task ShowCommandPaletteAsync(CancellationToken cancellationToken)
    {
        var viewModel = new CommandPaletteViewModel(_viewModel.CommandRegistry, GetWp03CommandState);
        var dialog = new CommandPaletteWindow(viewModel) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedCommandId is { } commandId)
        {
            await _wp03CommandDispatcher.ExecuteAsync(commandId, cancellationToken);
        }
        EditorContext.ActiveView?.Focus();
    }

    private void ShowWp03GotoLine()
    {
        var view = EditorContext.ActiveView;
        var target = view?.CommandTarget;
        if (view is null || target is null || !view.Document.AllowFindAndGoto) return;
        var dialog = new GotoLineDialog(target.CaretLine, view.Document.EditorDocument.LineCount) { Owner = this };
        if (dialog.ShowDialog() == true) target.GoToLine(dialog.LineNumber);
    }

    private async Task ExecuteSemanticEditorCommandAsync(RocketEditorCommand command, CancellationToken outerCancellation)
    {
        var view = EditorContext.ActiveView;
        var target = view?.CommandTarget;
        var document = view?.Document;
        if (view is null || target is null || document is null || !IsRocketPath(document.Path) || !document.AllowLsp) return;

        using var cancellation = BeginWp09Request();
        using var registration = outerCancellation.Register(cancellation.Cancel);
        try
        {
            var position = new LspPosition(Math.Max(0, target.CaretLine - 1), Math.Max(0, target.CaretColumn - 1));
            var range = SelectionToLspRange(document, target);
            switch (command)
            {
                case RocketEditorCommand.Definition:
                    await GoToDefinitionAsync(document.Path, position, cancellation.Token);
                    break;
                case RocketEditorCommand.References:
                    await FindReferencesAsync(document.Path, position, cancellation.Token);
                    break;
                case RocketEditorCommand.Rename:
                    await RenameSymbolAsync(document.Path, position, cancellation.Token);
                    break;
                case RocketEditorCommand.CodeActions:
                    await ShowCodeActionsAsync(null, document.Path, range, cancellation.Token);
                    break;
                case RocketEditorCommand.FormatDocument:
                    await FormatDocumentAsync(document.Path, cancellation.Token);
                    break;
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _wp09RequestCancellation, null, cancellation);
        }
    }

    private static LspRange SelectionToLspRange(DocumentTabViewModel document, IEditorCommandTarget target)
    {
        var text = document.EditorDocument;
        var start = Math.Clamp(target.SelectionStart, 0, text.TextLength);
        var end = Math.Clamp(start + target.SelectionLength, start, text.TextLength);
        if (target.SelectionLength == 0) start = end = Math.Clamp(target.CaretOffset, 0, text.TextLength);
        var startLocation = text.GetLocation(start);
        var endLocation = text.GetLocation(end);
        return new LspRange(
            new LspPosition(startLocation.Line - 1, startLocation.Column - 1),
            new LspPosition(endLocation.Line - 1, endLocation.Column - 1));
    }

    private async Task ShowFileSymbolsAsync(CancellationToken cancellationToken)
    {
        var document = EditorContext.ActiveDocument;
        if (document is null || !IsRocketPath(document.Path)) return;
        var snapshot = await _wp03DocumentSymbols.GetAsync(document, cancellationToken);
        if (snapshot is null)
        {
            SetLspStatus("LSP: document symbols unavailable");
            return;
        }
        using var viewModel = new SymbolSearchViewModel(SymbolSearchViewModel.FlattenDocumentSymbols(snapshot.Symbols));
        var dialog = new SymbolPickerWindow("Go to Symbol in File", viewModel) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedSymbol is { } selected)
        {
            if (_rocketSession.SessionGeneration != snapshot.SessionGeneration ||
                document.Version != snapshot.Version ||
                !_viewModel.Documents.Contains(document))
            {
                SetLspStatus("LSP: file symbols changed; search again");
                return;
            }
            await _wp03NavigationHistory.NavigateAsync(selected.Path, selected.Range, cancellationToken);
        }
    }

    private async Task ShowWorkspaceSymbolsAsync(CancellationToken cancellationToken)
    {
        var workspace = _viewModel.Explorer.Workspace?.Path;
        var generation = _rocketSession.SessionGeneration;
        using var viewModel = new SymbolSearchViewModel(async (query, token) =>
        {
            var symbols = await _rocketSession.RequestWorkspaceSymbolsAsync(query, token) ?? [];
            if (_rocketSession.SessionGeneration != generation ||
                !string.Equals(_viewModel.Explorer.Workspace?.Path, workspace, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Rocket workspace or language-server session changed. Search again.");
            return symbols.Select(symbol => SymbolSearchViewModel.FromWorkspaceSymbol(symbol, workspace)).ToArray();
        });
        var dialog = new SymbolPickerWindow("Go to Symbol in Workspace", viewModel) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedSymbol is { } selected)
        {
            if (_rocketSession.SessionGeneration != generation ||
                !string.Equals(_viewModel.Explorer.Workspace?.Path, workspace, StringComparison.OrdinalIgnoreCase))
            {
                SetLspStatus("LSP: workspace symbols changed; search again");
                return;
            }
            var status = await _rocketSession.RequestProjectStatusAsync(cancellationToken);
            if (!selected.SnapshotGeneration.HasValue || !status.IsSupported ||
                status.Status?.Generation != selected.SnapshotGeneration.Value ||
                _rocketSession.SessionGeneration != generation ||
                !string.Equals(_viewModel.Explorer.Workspace?.Path, workspace, StringComparison.OrdinalIgnoreCase))
            {
                SetLspStatus("LSP: workspace symbols changed; search again");
                return;
            }
            await _wp03NavigationHistory.NavigateAsync(selected.Path, selected.Range, cancellationToken);
        }
    }

    private void ShowOutline()
    {
        _ = _wp03Outline.RefreshAsync();
        if (_wp03OutlineWindow is { IsVisible: true })
        {
            _wp03OutlineWindow.Activate();
            return;
        }
        var window = new OutlineWindow(_wp03Outline) { Owner = this };
        window.Closed += (_, _) => _wp03OutlineWindow = null;
        _wp03OutlineWindow = window;
        window.Show();
    }

    private void Wp03NavigationHistory_Changed(object? sender, EventArgs e) => _viewModel.RaiseCommandStateChanged();

    private void Wp03DiagnosticSessionChanged(object? sender, RocketDiagnosticSessionChangedEventArgs e) =>
        DispatchUi(() =>
        {
            _wp03DocumentSymbols.Clear();
            _wp03FoldingProvider.NotifySessionChanged();
            _wp03UnsupportedProjectStatusGeneration = 0;
            _ = _wp03Outline.RefreshAsync();
            if (e.IsOnline) QueueWp03ProjectStatusRefresh();
        });

    private void Wp03DocumentSyncStateChanged(object? sender, RocketDocumentSyncStateChangedEventArgs e) =>
        DispatchUi(() =>
        {
            _wp03DocumentSymbols.Invalidate(e.Path);
            if (EditorContext.ActiveDocument is { } active && string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(e.Path), StringComparison.OrdinalIgnoreCase))
                _ = _wp03Outline.RefreshAsync();
        });

    private void QueueWp03ProjectStatusRefresh()
    {
        var generation = _rocketSession.SessionGeneration;
        if (generation <= 0 || generation == _wp03UnsupportedProjectStatusGeneration) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.WorkToken);
        var previous = Interlocked.Exchange(ref _wp03ProjectStatusCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        _ = RefreshWp03ProjectStatusAsync(generation, cancellation);
    }

    private async Task RefreshWp03ProjectStatusAsync(long generation, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(100, cancellation.Token);
            var result = await _rocketSession.RequestProjectStatusAsync(cancellation.Token);
            if (_rocketSession.SessionGeneration != generation || cancellation.IsCancellationRequested) return;
            if (!result.IsSupported)
            {
                _wp03UnsupportedProjectStatusGeneration = generation;
                return;
            }
            if (result.Status is not { } status) return;
            DispatchUi(() =>
            {
                var overFiles = status.MaximumProjectFiles > 0 && status.Files > status.MaximumProjectFiles;
                var overBytes = status.MaximumProjectBytes > 0 && status.Bytes > status.MaximumProjectBytes;
                SetLspStatus(overFiles || overBytes
                    ? $"LSP: project limit · {status.Files}/{status.MaximumProjectFiles} files · {status.Bytes}/{status.MaximumProjectBytes} bytes"
                    : $"LSP: project · {status.Files} files · {status.Symbols} symbols");
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _wp03ProjectStatusCancellation, null, cancellation), cancellation))
                cancellation.Dispose();
        }
    }

    private void DisposeWp03()
    {
        _wp03NavigationHistory.Changed -= Wp03NavigationHistory_Changed;
        _rocketSession.DiagnosticSessionChanged -= Wp03DiagnosticSessionChanged;
        _rocketSession.DocumentSyncStateChanged -= Wp03DocumentSyncStateChanged;
        _wp03Outline.Dispose();
        _wp03OutlineWindow?.Close();
        var cancellation = Interlocked.Exchange(ref _wp03ProjectStatusCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
