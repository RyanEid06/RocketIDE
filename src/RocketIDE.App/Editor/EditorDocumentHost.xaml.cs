using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using RocketIDE.App.Editor.Completion;
using RocketIDE.App.Editor.Hover;
using RocketIDE.App.Editor.SemanticTokens;
using RocketIDE.App.Editor.SignatureHelp;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Editor;

public partial class EditorDocumentHost : UserControl, IEditorCommandTarget
{
    private readonly DiagnosticRenderer _diagnosticRenderer;
    private readonly BracketMatchRenderer _bracketMatchRenderer;
    private readonly DebugMarkerRenderer _debugMarkerRenderer;
    private readonly RocketCompletionController _completionController;
    private readonly RocketHoverController _hoverController;
    private readonly RocketSignatureHelpController _signatureHelpController;
    private readonly RocketSemanticTokenController _semanticTokenController;
    private RocketFoldingController? _foldingController;
    private IRocketFoldingRangeProvider? _attachedFoldingProvider;
    private DocumentTabViewModel? _document;
    private EditorViewViewModel? _view;
    private bool _rocketFeaturesAttached;
    private bool _restoringViewState;

    public event EventHandler? ViewActivated;
    public event EventHandler<RocketEditorCommandRequestedEventArgs>? RocketCommandRequested;

    public static readonly DependencyProperty FeatureServiceProperty = DependencyProperty.Register(
        nameof(FeatureService),
        typeof(IRocketEditorFeatureService),
        typeof(EditorDocumentHost),
        new PropertyMetadata(null, FeatureServiceChanged));

    public IRocketEditorFeatureService? FeatureService
    {
        get => (IRocketEditorFeatureService?)GetValue(FeatureServiceProperty);
        set => SetValue(FeatureServiceProperty, value);
    }

    public static readonly DependencyProperty FoldingProviderProperty = DependencyProperty.Register(
        nameof(FoldingProvider),
        typeof(IRocketFoldingRangeProvider),
        typeof(EditorDocumentHost),
        new PropertyMetadata(null, FoldingProviderChanged));

    public IRocketFoldingRangeProvider? FoldingProvider
    {
        get => (IRocketFoldingRangeProvider?)GetValue(FoldingProviderProperty);
        set => SetValue(FoldingProviderProperty, value);
    }

    public EditorDocumentHost()
    {
        InitializeComponent();
        _diagnosticRenderer = new DiagnosticRenderer(Editor);
        _bracketMatchRenderer = new BracketMatchRenderer(Editor);
        _debugMarkerRenderer = new DebugMarkerRenderer(Editor);
        _completionController = new RocketCompletionController(Editor, () => FeatureService, () => _view);
        _hoverController = new RocketHoverController(Editor, () => FeatureService);
        _signatureHelpController = new RocketSignatureHelpController(Editor, () => FeatureService);
        _semanticTokenController = new RocketSemanticTokenController(Editor, () => FeatureService);
        DataContextChanged += OnDataContextChanged;
        Loaded += EditorDocumentHost_Loaded;
        Unloaded += EditorDocumentHost_Unloaded;
        Editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        Editor.TextArea.SelectionChanged += Editor_SelectionChanged;
        Editor.TextArea.TextView.ScrollOffsetChanged += TextView_ScrollOffsetChanged;
        Editor.GotKeyboardFocus += Editor_GotKeyboardFocus;
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.TextView.CurrentLineBackground = (Brush)FindResource("IDE.ChromeRaisedBrush");
    }

    public int EditorLineCount => Editor.Document.LineCount;

    public int CaretLine => Editor.TextArea.Caret.Line;

    public int CaretColumn => Editor.TextArea.Caret.Column;

    public int CaretOffset => Editor.CaretOffset;

    public int SelectionStart => Editor.SelectionStart;

    public int SelectionLength => Editor.SelectionLength;

    public void Undo() => Editor.Undo();

    public void Redo() => Editor.Redo();

    public void SelectAll() => Editor.SelectAll();

    public void SetSelection(int startOffset, int length)
    {
        var start = Math.Clamp(startOffset, 0, Editor.Document.TextLength);
        var clampedLength = Math.Clamp(length, 0, Editor.Document.TextLength - start);
        Editor.Select(start, clampedLength);
    }

    public void FocusEditor() => Editor.Focus();

    public void ShowFind(bool includeReplace)
    {
        if (_document is { AllowFindAndGoto: false })
        {
            return;
        }

        if (string.IsNullOrEmpty(FindTextBox.Text) && !string.IsNullOrEmpty(Editor.SelectedText))
        {
            FindTextBox.Text = Editor.SelectedText;
        }

        FindBar.Visibility = Visibility.Visible;
        ReplaceControls.Visibility = includeReplace ? Visibility.Visible : Visibility.Collapsed;
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    public void GoToLine(int line)
    {
        if (_document is { AllowFindAndGoto: false })
        {
            return;
        }

        var clampedLine = Math.Clamp(line, 1, Math.Max(1, Editor.Document.LineCount));
        var documentLine = Editor.Document.GetLineByNumber(clampedLine);
        Editor.TextArea.Caret.Offset = documentLine.Offset;
        Editor.ScrollTo(clampedLine, 1);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    public void RequestRocketCommand(RocketEditorCommand command)
    {
        if (_document is not { } document || !IsRocketDocument(document))
        {
            return;
        }

        var caret = new LspPosition(
            Math.Max(0, Editor.TextArea.Caret.Line - 1),
            Math.Max(0, Editor.TextArea.Caret.Column - 1));
        var selectionStart = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
        var selectionEnd = Editor.SelectionLength > 0 ? Editor.SelectionStart + Editor.SelectionLength : Editor.CaretOffset;
        var startLocation = Editor.Document.GetLocation(selectionStart);
        var endLocation = Editor.Document.GetLocation(selectionEnd);
        var range = new LspRange(
            new LspPosition(startLocation.Line - 1, startLocation.Column - 1),
            new LspPosition(endLocation.Line - 1, endLocation.Column - 1));
        RocketCommandRequested?.Invoke(this, new RocketEditorCommandRequestedEventArgs(command, document.Path, caret, range));
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachDocument();
        if (e.NewValue is EditorViewViewModel view)
        {
            AttachView(view);
        }
        else
        {
            Editor.Document = new TextDocument();
            Editor.SyntaxHighlighting = null;
            Editor.Options.ConvertTabsToSpaces = false;
            _diagnosticRenderer.UpdateDiagnostics([]);
        }

        ReportViewState();
        _bracketMatchRenderer.Update();
    }

    private void AttachView(EditorViewViewModel view)
    {
        // Document assignment and caret/selection restoration raise synchronous
        // editor events. Do not let those intermediate states replace the saved view.
        _restoringViewState = true;
        try
        {
            AttachViewCore(view);
        }
        finally
        {
            _restoringViewState = false;
        }
        ReportViewState();
    }

    private void AttachViewCore(EditorViewViewModel view)
    {
        _view = view;
        _view.AttachCommandTarget(this);
        _view.FocusRequested += View_FocusRequested;
        _view.NavigationRequested += View_NavigationRequested;

        var document = view.Document;
        _document = document;
        _document.PropertyChanged += Document_PropertyChanged;
        _document.DiagnosticsChanged += Document_DiagnosticsChanged;
        _document.DebugMarkersChanged += Document_DebugMarkersChanged;
        Editor.Document = document.EditorDocument;
        if (IsRocketDocument(document))
        {
            if (document.AllowSyntaxColoring)
            {
                try
                {
                    Editor.SyntaxHighlighting = RocketSyntaxHighlighting.Definition;
                }
                catch (Exception exception)
                {
                    Trace.TraceError($"Rocket syntax highlighting failed to load: {exception}");
                    Editor.SyntaxHighlighting = null;
                }
            }
            else
            {
                Editor.SyntaxHighlighting = null;
            }

            if (document.AllowLocalEditing)
            {
                RocketIndentationStrategy.Configure(Editor);
            }
            _diagnosticRenderer.UpdateDiagnostics(document.Diagnostics);
            if (document.AllowLsp)
            {
                AttachRocketFeatures(document);
            }
            _debugMarkerRenderer.UpdateMarkers(document.DebugBreakpointLines, document.DebugCurrentLine);
        }
        else
        {
            Editor.SyntaxHighlighting = null;
            Editor.Options.ConvertTabsToSpaces = false;
            _diagnosticRenderer.UpdateDiagnostics([]);
        }

        RestoreViewState(view);
        ApplyPendingNavigation(view);
        ReportViewState();
    }

    private void EditorDocumentHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (_document is null && DataContext is EditorViewViewModel view)
        {
            AttachView(view);
        }
    }

    private void EditorDocumentHost_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachDocument();
        _diagnosticRenderer.UpdateDiagnostics([]);
    }

    private void DetachDocument()
    {
        DetachRocketFeatures();
        if (_view is not null)
        {
            _view.FocusRequested -= View_FocusRequested;
            _view.NavigationRequested -= View_NavigationRequested;
            _view.DetachCommandTarget(this);
            _view = null;
        }
        if (_document is null) return;

        _document.PropertyChanged -= Document_PropertyChanged;
        _document.DiagnosticsChanged -= Document_DiagnosticsChanged;
        _document.DebugMarkersChanged -= Document_DebugMarkersChanged;
        _document = null;
        _debugMarkerRenderer.UpdateMarkers([], null);
    }

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentTabViewModel document || !ReferenceEquals(document, _document) ||
            e.PropertyName != nameof(DocumentTabViewModel.AllowLsp) || !IsRocketDocument(document))
        {
            return;
        }

        if (document.AllowLsp && !_rocketFeaturesAttached)
        {
            AttachRocketFeatures(document);
        }
        else if (!document.AllowLsp && _rocketFeaturesAttached)
        {
            DetachRocketFeatures();
        }
    }

    private void Document_DebugMarkersChanged(object? sender, EventArgs e)
    {
        if (sender is DocumentTabViewModel document && ReferenceEquals(document, _document))
        {
            _debugMarkerRenderer.UpdateMarkers(document.DebugBreakpointLines, document.DebugCurrentLine);
        }
    }

    private void Document_DiagnosticsChanged(object? sender, EventArgs e)
    {
        if (sender is DocumentTabViewModel document && ReferenceEquals(document, _document))
        {
            _diagnosticRenderer.UpdateDiagnostics(document.Diagnostics);
        }
    }

    private void View_NavigationRequested(object? sender, EditorViewNavigationRequestedEventArgs e)
    {
        if (sender is EditorViewViewModel view && ReferenceEquals(view, _view))
        {
            NavigateTo(e.Range);
            _ = view.TakePendingNavigation();
        }
    }

    private void ApplyPendingNavigation(EditorViewViewModel view)
    {
        if (view.TakePendingNavigation() is { } pending)
        {
            NavigateTo(pending);
        }
    }

    private void RestoreViewState(EditorViewViewModel view)
    {
        var length = Editor.Document.TextLength;
        var caret = Math.Clamp(view.CaretOffset, 0, length);
        SetSelection(Math.Clamp(view.SelectionStart, 0, length), view.SelectionLength);
        Editor.CaretOffset = caret;
        var scrollInfo = (IScrollInfo)Editor.TextArea.TextView;
        scrollInfo.SetHorizontalOffset(view.HorizontalOffset);
        scrollInfo.SetVerticalOffset(view.VerticalOffset);
    }

    private void NavigateTo(SourceRange range)
    {
        if (!DiagnosticRenderer.TryGetOffsetRange(Editor.Document, range, out var startOffset, out var length))
        {
            return;
        }

        Editor.Select(startOffset, length);
        Editor.TextArea.Caret.Offset = startOffset;
        var location = Editor.Document.GetLocation(startOffset);
        Editor.ScrollTo(location.Line, location.Column);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        ReportViewState();
        _bracketMatchRenderer.Update();
    }

    private void Editor_SelectionChanged(object? sender, EventArgs e) => ReportViewState();

    private void TextView_ScrollOffsetChanged(object? sender, EventArgs e) => ReportViewState();

    private void Editor_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ViewActivated?.Invoke(this, EventArgs.Empty);
        ReportViewState();
    }

    private void View_FocusRequested(object? sender, EventArgs e) => Editor.Focus();

    private void ReportViewState()
    {
        if (_view is null || _restoringViewState) return;
        _view.UpdateCaret(Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column, Editor.CaretOffset);
        _view.UpdateSelection(Editor.SelectionStart, Editor.SelectionLength);
        _view.UpdateScroll(Editor.TextArea.TextView.HorizontalOffset, Editor.TextArea.TextView.VerticalOffset);
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
        {
            CloseFind();
            e.Handled = true;
            return;
        }

        if (_document is { } document && IsRocketDocument(document))
        {
            if (!document.AllowLocalEditing)
            {
                return;
            }

            if (RocketEditorCommandBinding.TryGetCommand(e.Key, Keyboard.Modifiers, out var command))
            {
                RequestRocketCommand(command);
                e.Handled = true;
                return;
            }

            // AvalonEdit's completion input handler must receive Tab/Enter before
            // the editor applies indentation or inserts a newline.
            if (!_completionController.IsOpen && EditorKeyBehavior.HandlePreviewKeyDown(Editor, e))
            {
                e.Handled = true;
            }
        }
    }

    private void Editor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_document is { } document &&
            document.AllowLocalEditing &&
            IsRocketDocument(document) &&
            EditorKeyBehavior.HandleTextInput(Editor, e.Text, _signatureHelpController.NotifyHandledTextInput))
        {
            e.Handled = true;
        }
    }

    private void AttachRocketFeatures(DocumentTabViewModel document)
    {
        if (_rocketFeaturesAttached)
        {
            return;
        }

        _completionController.Attach(document);
        _hoverController.Attach(document);
        _signatureHelpController.Attach(document);
        _semanticTokenController.Attach(document);
        if (FoldingProvider is { } foldingProvider)
        {
            _foldingController = new RocketFoldingController(
                Editor,
                new RocketFoldingRangeAdapter(foldingProvider, () => _document));
            _foldingController.Attach(document);
            _foldingController.RestoreCollapsedState(
                _view?.CollapsedFolds.Select(range => new EditorFoldingRange(range.StartLine, range.EndLine)) ?? []);
            if (_view is { } view)
            {
                view.SetFoldingStateCapture(CaptureCollapsedFolds);
            }
            foldingProvider.SessionChanged += FoldingProvider_SessionChanged;
            _attachedFoldingProvider = foldingProvider;
        }
        _rocketFeaturesAttached = true;
    }

    private void DetachRocketFeatures()
    {
        if (!_rocketFeaturesAttached)
        {
            return;
        }

        _completionController.Detach();
        _hoverController.Detach();
        _signatureHelpController.Detach();
        _semanticTokenController.Detach();
        if (_foldingController is { } foldingController)
        {
            _view?.SetCollapsedFolds(CaptureCollapsedFolds());
            _view?.SetFoldingStateCapture(null);
            if (_attachedFoldingProvider is { } foldingProvider)
                foldingProvider.SessionChanged -= FoldingProvider_SessionChanged;
            _attachedFoldingProvider = null;
            foldingController.Dispose();
            _foldingController = null;
        }
        _rocketFeaturesAttached = false;
    }

    private IReadOnlyList<SourceRange> CaptureCollapsedFolds() =>
        _foldingController?.CaptureCollapsedState()
            .Select(range => new SourceRange(range.StartLine, 0, range.EndLine, 0))
            .ToArray() ?? [];

    private void FoldingProvider_SessionChanged(object? sender, EventArgs e) =>
        _foldingController?.NotifySessionChanged();

    private static void FeatureServiceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is EditorDocumentHost host && host._document is { } document &&
            IsRocketDocument(document) && document.AllowLsp)
        {
            host.DetachRocketFeatures();
            host.AttachRocketFeatures(document);
        }
    }

    private static void FoldingProviderChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is EditorDocumentHost host && host._document is { } document &&
            IsRocketDocument(document) && document.AllowLsp)
        {
            host.DetachRocketFeatures();
            host.AttachRocketFeatures(document);
        }
    }

    private static bool IsRocketDocument(DocumentTabViewModel document) =>
        string.Equals(System.IO.Path.GetExtension(document.Path), ".rocket", StringComparison.OrdinalIgnoreCase);

    private void Definition_Click(object sender, RoutedEventArgs e) => RequestRocketCommand(RocketEditorCommand.Definition);
    private void References_Click(object sender, RoutedEventArgs e) => RequestRocketCommand(RocketEditorCommand.References);
    private void RenameSymbol_Click(object sender, RoutedEventArgs e) => RequestRocketCommand(RocketEditorCommand.Rename);
    private void CodeActions_Click(object sender, RoutedEventArgs e) => RequestRocketCommand(RocketEditorCommand.CodeActions);
    private void FormatDocument_Click(object sender, RoutedEventArgs e) => RequestRocketCommand(RocketEditorCommand.FormatDocument);

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        FindNext(backwards: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext(backwards: false);

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindNext(backwards: true);

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var pattern = FindTextBox.Text;
        if (string.IsNullOrEmpty(pattern))
        {
            return;
        }

        var comparison = MatchCaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (string.Equals(Editor.SelectedText, pattern, comparison))
        {
            Editor.SelectedText = ReplaceTextBox.Text;
        }

        FindNext(backwards: false);
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var pattern = FindTextBox.Text;
        if (string.IsNullOrEmpty(pattern))
        {
            return;
        }

        var comparison = MatchCaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var replaced = Editor.Text.Replace(pattern, ReplaceTextBox.Text, comparison);
        if (string.Equals(replaced, Editor.Text, StringComparison.Ordinal))
        {
            return;
        }

        using (Editor.Document.RunUpdate())
        {
            Editor.Document.Replace(0, Editor.Document.TextLength, replaced);
        }
    }

    private void CloseFind_Click(object sender, RoutedEventArgs e) => CloseFind();

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ReplaceControls.Visibility = Visibility.Collapsed;
        Editor.Focus();
    }

    private void FindNext(bool backwards)
    {
        var pattern = FindTextBox.Text;
        var text = Editor.Text;
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text) || pattern.Length > text.Length)
        {
            return;
        }

        var comparison = MatchCaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var index = backwards
            ? FindPreviousIndex(text, pattern, comparison)
            : FindNextIndex(text, pattern, comparison);

        if (index < 0)
        {
            return;
        }

        Editor.Select(index, pattern.Length);
        var line = Editor.Document.GetLineByOffset(index).LineNumber;
        Editor.ScrollToLine(line);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    private int FindNextIndex(string text, string pattern, StringComparison comparison)
    {
        var start = Editor.SelectionLength > 0
            ? Editor.SelectionStart + Editor.SelectionLength
            : Editor.CaretOffset;

        var index = start <= text.Length - pattern.Length
            ? text.IndexOf(pattern, start, comparison)
            : -1;

        return index >= 0 ? index : text.IndexOf(pattern, comparison);
    }

    private int FindPreviousIndex(string text, string pattern, StringComparison comparison)
    {
        var start = Editor.SelectionLength > 0
            ? Editor.SelectionStart - 1
            : Math.Min(Editor.CaretOffset - 1, text.Length - 1);

        var index = start >= 0
            ? text.LastIndexOf(pattern, start, comparison)
            : -1;

        return index >= 0 ? index : text.LastIndexOf(pattern, comparison);
    }
}
