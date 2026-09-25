using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using RocketIDE.App.Editor.Snippets;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Editor.Completion;

internal sealed class RocketSnippetCompletionData(
    RocketSnippetDefinition snippet,
    RocketSnippetService service,
    Func<EditorViewViewModel?> viewProvider,
    int replaceOffset,
    int replaceLength) : ICompletionData
{
    public ImageSource? Image => null;
    public string Text => snippet.Trigger;
    public object Content => $"{snippet.Trigger}  snippet";
    public object Description => snippet.Description;
    public double Priority => 1;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var view = viewProvider();
        if (view is null || !RocketSnippetService.CanInsert(view))
            return;

        service.Insert(view, snippet, replaceOffset, replaceLength);
    }
}
