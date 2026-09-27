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
    private RocketCommandDispatcher _navigationCommandDispatcher = null!;
    private DocumentSymbolService _navigationDocumentSymbols = null!;
    private RocketFoldingRangeProvider _navigationFoldingProvider = null!;
    private NavigationHistoryService _navigationNavigationHistory = null!;
    private OutlineViewModel _navigationOutline = null!;
    private OutlineWindow? _navigationOutlineWindow;
    private CancellationTokenSource? _navigationProjectStatusCancellation;
    private long _navigationUnsupportedProjectStatusGeneration;

    public IRocketFoldingRangeProvider RocketFoldingRanges => _navigationFoldingProvider;

    private void InitializeNavigationFeatures()
    {
        _navigationDocumentSymbols = new DocumentSymbolService(
            _rocketSession.RequestDocumentSymbolsAsync,
            () => _rocketSession.SessionGeneration);
        _navigationFoldingProvider = new RocketFoldingRangeProvider(
            _rocketSession.RequestFoldingRangesAsync,
            () => _rocketSession.SessionGeneration,
            _lifetime.WorkToken);
        var validatedNavigation = new ValidatedEditorNavigation(
            EditorNavigation,
            async (path, token) => FindOpenDocument(path)?.Text ?? await ReadNavigationTextAsync(path, token),
            message => AppendRocketOutput(message));
        _navigationNavigationHistory = new NavigationHistoryService(EditorContext, validatedNavigation, 100);
        _navigationOutline = new OutlineViewModel(EditorContext, _navigationDocumentSymbols, _navigationNavigationHistory, _lifetime.WorkToken);
        _navigationCommandDispatcher = new RocketCommandDispatcher(_viewModel.CommandRegistry, GetNavigationFeaturesCommandState);
        RegisterNavigationFeaturesCommands();
        _navigationNavigationHistory.Changed += NavigationFeaturesNavigationHistory_Changed;
        _rocketSession.DiagnosticSessionChanged += NavigationFeaturesDiagnosticSessionChanged;
        _rocketSession.DocumentSyncStateChanged += NavigationFeaturesDocumentSyncStateChanged;
        Closed += (_, _) => DisposeNavigationFeatures();
    }

    private RocketCommandState GetNavigationFeaturesCommandState(string commandId)
    {
        if (string.Equals(commandId, RocketCommandRegistry.NavigateBack, StringComparison.Ordinal))
            return new RocketCommandState(commandId, _navigationNavigationHistory.CanGoBack, false);
        if (string.Equals(commandId, RocketCommandRegistry.NavigateForward, StringComparison.Ordinal))
            return new RocketCommandState(commandId, _navigationNavigationHistory.CanGoForward, false);
        return _viewModel.GetCommandState(commandId);
    }

    private void RegisterNavigationFeaturesCommands()
    {
        void Register(string id, Action action) => _navigationCommandDispatcher.Register(id, _ =>
        {
            action();
            return Task.CompletedTask;
        });

        _navigationCommandDispatcher.Register(RocketCommandRegistry.CommandPalette, ShowCommandPaletteAsync);
        Register(RocketCommandRegistry.Undo, () => EditorContext.ActiveView?.CommandTarget?.Undo());
        Register(RocketCommandRegistry.Redo, () => EditorContext.ActiveView?.CommandTarget?.Redo());
        Register(RocketCommandRegistry.SelectAll, () => EditorContext.ActiveView?.CommandTarget?.SelectAll());
        Register(RocketCommandRegistry.Find, () => EditorContext.ActiveView?.CommandTarget?.ShowFind(includeReplace: false));
        Register(RocketCommandRegistry.ReplaceDocument, () => EditorContext.ActiveView?.CommandTarget?.ShowFind(includeReplace: true));
        Register(RocketCommandRegistry.GoToLine, ShowNavigationFeaturesGotoLine);
        _navigationCommandDispatcher.Register(RocketCommandRegistry.GoToDefinition, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.Definition, token));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.FindReferences, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.References, token));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.RenameSymbol, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.Rename, token));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.CodeActions, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.CodeActions, token));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.FormatDocument, token => ExecuteSemanticEditorCommandAsync(RocketEditorCommand.FormatDocument, token));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.GoToSymbolFile, ShowFileSymbolsAsync);
        _navigationCommandDispatcher.Register(RocketCommandRegistry.GoToSymbolWorkspace, ShowWorkspaceSymbolsAsync);
        _navigationCommandDispatcher.Register(RocketCommandRegistry.NavigateBack, token => _navigationNavigationHistory.GoBackAsync(token));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.NavigateForward, token => _navigationNavigationHistory.GoForwardAsync(token));
        Register(RocketCommandRegistry.Outline, ShowOutline);

        _navigationCommandDispatcher.Register(RocketCommandRegistry.Check, _ => ExecuteRocketCommandAsync(RocketCommandKind.Check));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.Build, _ => ExecuteRocketCommandAsync(RocketCommandKind.Build));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.Run, _ => ExecuteRocketCommandAsync(RocketCommandKind.Run));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.Test, _ => ExecuteRocketCommandAsync(RocketCommandKind.Test));
        Register(RocketCommandRegistry.Stop, () => StopRocket_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.NewProject, () => NewRocketProject_Click(this, new RoutedEventArgs()));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.Resolve, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Resolve, new RocketAdvancedCommandOptions()));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.DependencyTree, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Tree, new RocketAdvancedCommandOptions()));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.Audit, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Audit, new RocketAdvancedCommandOptions()));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.TargetInfo, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Target, new RocketAdvancedCommandOptions(Verbose: true)));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.FormatTarget, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Format, new RocketAdvancedCommandOptions()));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.Coverage, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Coverage, CreateMeasurementOptions("coverage")));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.Profile, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Profile, CreateMeasurementOptions("profile")));
        _navigationCommandDispatcher.Register(RocketCommandRegistry.Benchmark, _ => ExecuteAdvancedRocketCommandAsync(RocketAdvancedCommandKind.Benchmark, CreateMeasurementOptions("benchmark")));
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
        Register(RocketCommandRegistry.DebugRestart, () => DebugRestart_Click(this, new RoutedEventArgs()));
        Register(RocketCommandRegistry.DebugRunToCursor, () => DebugRunToCursor_Click(this, new RoutedEventArgs()));
    }

    private async void RegisteredCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string commandId } || !_navigationCommandDispatcher.IsRegistered(commandId)) return;
        try
        {
            await _navigationCommandDispatcher.ExecuteAsync(commandId, _lifetime.WorkToken);
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

    private async Task<bool> TryDispatchNavigationFeaturesGestureAsync(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var gesture = FormatNavigationFeaturesGesture(key, Keyboard.Modifiers);
        if (string.IsNullOrEmpty(gesture)) return false;
        var definition = _viewModel.CommandRegistry.FindByGesture(gesture);
        if (definition is null || !_navigationCommandDispatcher.IsRegistered(definition.Id)) return false;
        e.Handled = true;
        try
        {
            await _navigationCommandDispatcher.ExecuteAsync(definition.Id, _lifetime.WorkToken);
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

    private static string FormatNavigationFeaturesGesture(Key key, ModifierKeys modifiers)
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
        var viewModel = new CommandPaletteViewModel(_viewModel.CommandRegistry, GetNavigationFeaturesCommandState);
        var dialog = new CommandPaletteWindow(viewModel) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedCommandId is { } commandId)
        {
            await _navigationCommandDispatcher.ExecuteAsync(commandId, cancellationToken);
        }
        EditorContext.ActiveView?.Focus();
    }

    private void ShowNavigationFeaturesGotoLine()
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

        using var cancellation = BeginSemanticEditingRequest();
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
            Interlocked.CompareExchange(ref _semanticRequestCancellation, null, cancellation);
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
        var snapshot = await _navigationDocumentSymbols.GetAsync(document, cancellationToken);
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
            await _navigationNavigationHistory.NavigateAsync(selected.Path, selected.Range, cancellationToken);
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
            await _navigationNavigationHistory.NavigateAsync(selected.Path, selected.Range, cancellationToken);
        }
    }

    private void ShowOutline()
    {
        _ = _navigationOutline.RefreshAsync();
        if (_navigationOutlineWindow is { IsVisible: true })
        {
            _navigationOutlineWindow.Activate();
            return;
        }
        var window = new OutlineWindow(_navigationOutline) { Owner = this };
        window.Closed += (_, _) => _navigationOutlineWindow = null;
        _navigationOutlineWindow = window;
        window.Show();
    }

    private void NavigationFeaturesNavigationHistory_Changed(object? sender, EventArgs e) => _viewModel.RaiseCommandStateChanged();

    private void NavigationFeaturesDiagnosticSessionChanged(object? sender, RocketDiagnosticSessionChangedEventArgs e) =>
        DispatchUi(() =>
        {
            _navigationDocumentSymbols.Clear();
            _navigationFoldingProvider.NotifySessionChanged();
            _navigationUnsupportedProjectStatusGeneration = 0;
            _ = _navigationOutline.RefreshAsync();
            if (e.IsOnline) QueueNavigationFeaturesProjectStatusRefresh();
        });

    private void NavigationFeaturesDocumentSyncStateChanged(object? sender, RocketDocumentSyncStateChangedEventArgs e) =>
        DispatchUi(() =>
        {
            _navigationDocumentSymbols.Invalidate(e.Path);
            if (EditorContext.ActiveDocument is { } active && string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(e.Path), StringComparison.OrdinalIgnoreCase))
                _ = _navigationOutline.RefreshAsync();
        });

    private void QueueNavigationFeaturesProjectStatusRefresh()
    {
        var generation = _rocketSession.SessionGeneration;
        if (generation <= 0 || generation == _navigationUnsupportedProjectStatusGeneration) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.WorkToken);
        var previous = Interlocked.Exchange(ref _navigationProjectStatusCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        _ = RefreshNavigationFeaturesProjectStatusAsync(generation, cancellation);
    }

    private async Task RefreshNavigationFeaturesProjectStatusAsync(long generation, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(100, cancellation.Token);
            var result = await _rocketSession.RequestProjectStatusAsync(cancellation.Token);
            if (_rocketSession.SessionGeneration != generation || cancellation.IsCancellationRequested) return;
            if (!result.IsSupported)
            {
                _navigationUnsupportedProjectStatusGeneration = generation;
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
            if (ReferenceEquals(Interlocked.CompareExchange(ref _navigationProjectStatusCancellation, null, cancellation), cancellation))
                cancellation.Dispose();
        }
    }

    private void DisposeNavigationFeatures()
    {
        _navigationNavigationHistory.Changed -= NavigationFeaturesNavigationHistory_Changed;
        _rocketSession.DiagnosticSessionChanged -= NavigationFeaturesDiagnosticSessionChanged;
        _rocketSession.DocumentSyncStateChanged -= NavigationFeaturesDocumentSyncStateChanged;
        _navigationOutline.Dispose();
        _navigationOutlineWindow?.Close();
        var cancellation = Interlocked.Exchange(ref _navigationProjectStatusCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
