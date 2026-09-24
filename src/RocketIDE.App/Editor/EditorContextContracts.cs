using RocketIDE.App.ViewModels;
using RocketIDE.Core.Diagnostics;

namespace RocketIDE.App.Editor;

public interface IEditorCommandTarget
{
    int CaretLine { get; }
    int CaretColumn { get; }
    int CaretOffset { get; }
    int SelectionStart { get; }
    int SelectionLength { get; }

    void SetSelection(int startOffset, int length);
    void FocusEditor();
    void Undo();
    void Redo();
    void SelectAll();
    void ShowFind(bool includeReplace);
    void GoToLine(int line);
}

public interface IEditorViewContext
{
    DocumentTabViewModel Document { get; }
    IEditorCommandTarget? CommandTarget { get; }

    void Focus();
}

public interface IEditorContext
{
    IEditorViewContext? ActiveView { get; }
    DocumentTabViewModel? ActiveDocument { get; }

    event EventHandler? ActiveContextChanged;
}

public interface IEditorNavigation
{
    Task<IEditorViewContext?> OpenOrRevealAsync(
        string path,
        SourceRange? range = null,
        CancellationToken cancellationToken = default);
}
