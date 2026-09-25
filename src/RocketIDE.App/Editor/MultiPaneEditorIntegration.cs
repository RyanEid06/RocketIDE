using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;

namespace RocketIDE.App.Editor;

public sealed class MultiPaneEditorIntegration : IEditorContext, IEditorNavigation
{
    private readonly EditorLayoutViewModel _layout;
    private readonly Func<string, CancellationToken, Task<DocumentTabViewModel?>> _openDocumentAsync;

    public MultiPaneEditorIntegration(
        EditorLayoutViewModel layout,
        Func<string, CancellationToken, Task<DocumentTabViewModel?>> openDocumentAsync)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _openDocumentAsync = openDocumentAsync ?? throw new ArgumentNullException(nameof(openDocumentAsync));
        _layout.ActiveContextChanged += (_, _) => ActiveContextChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ActiveContextChanged;
    public IEditorViewContext? ActiveView => _layout.ActiveView;
    public DocumentTabViewModel? ActiveDocument => _layout.ActiveView?.Document;

    public async Task<IEditorViewContext?> OpenOrRevealAsync(
        string path,
        SourceRange? range = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var document = await _openDocumentAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (document is null) return null;

        var view = _layout.OpenOrActivate(document);
        if (range is not null) view.RequestNavigation(range);
        view.Focus();
        return view;
    }
}
