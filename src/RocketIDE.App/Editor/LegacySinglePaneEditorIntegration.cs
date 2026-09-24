using System.ComponentModel;
using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;

namespace RocketIDE.App.Editor;

public sealed class LegacySinglePaneEditorIntegration : IEditorContext, IEditorNavigation
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<DocumentTabViewModel, IEditorCommandTarget?> _resolveCommandTarget;
    private readonly Func<string, CancellationToken, Task<DocumentTabViewModel?>> _openDocumentAsync;
    private LegacySinglePaneEditorViewContext? _activeView;

    public LegacySinglePaneEditorIntegration(
        MainWindowViewModel viewModel,
        Func<DocumentTabViewModel, IEditorCommandTarget?> resolveCommandTarget,
        Func<string, CancellationToken, Task<DocumentTabViewModel?>> openDocumentAsync)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _resolveCommandTarget = resolveCommandTarget ?? throw new ArgumentNullException(nameof(resolveCommandTarget));
        _openDocumentAsync = openDocumentAsync ?? throw new ArgumentNullException(nameof(openDocumentAsync));
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public event EventHandler? ActiveContextChanged;

    public IEditorViewContext? ActiveView => GetOrCreateView(_viewModel.ActiveDocument);

    public DocumentTabViewModel? ActiveDocument => ActiveView?.Document;

    public async Task<IEditorViewContext?> OpenOrRevealAsync(
        string path,
        SourceRange? range = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var document = await _openDocumentAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (document is null)
        {
            return null;
        }

        if (!ReferenceEquals(_viewModel.ActiveDocument, document))
        {
            _viewModel.ActiveDocument = document;
        }

        var view = GetOrCreateView(document);
        if (view is null)
        {
            return null;
        }

        if (range is not null)
        {
            document.RequestNavigation(range);
        }

        view.Focus();
        return view;
    }

    private LegacySinglePaneEditorViewContext? GetOrCreateView(DocumentTabViewModel? document)
    {
        if (document is null)
        {
            _activeView = null;
            return null;
        }

        if (_activeView is null || !ReferenceEquals(_activeView.Document, document))
        {
            _activeView = new LegacySinglePaneEditorViewContext(document, _resolveCommandTarget);
        }

        return _activeView;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.ActiveDocument))
        {
            return;
        }

        _activeView = null;
        ActiveContextChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class LegacySinglePaneEditorViewContext(
        DocumentTabViewModel document,
        Func<DocumentTabViewModel, IEditorCommandTarget?> resolveCommandTarget) : IEditorViewContext
    {
        public DocumentTabViewModel Document { get; } = document ?? throw new ArgumentNullException(nameof(document));

        public IEditorCommandTarget? CommandTarget => resolveCommandTarget(Document);

        public void Focus() => CommandTarget?.FocusEditor();
    }
}
