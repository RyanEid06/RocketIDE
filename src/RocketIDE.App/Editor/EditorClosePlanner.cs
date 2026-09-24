using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Editor;

public static class EditorClosePlanner
{
    public static IReadOnlyList<DocumentTabViewModel> LogicalDocumentsClosing(
        EditorLayoutViewModel layout,
        IEnumerable<EditorViewViewModel> requestedViews)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(requestedViews);
        var views = requestedViews.Distinct().ToArray();
        return views
            .GroupBy(view => view.Document)
            .Where(group => layout.CountViews(group.Key) == group.Count())
            .Select(group => group.Key)
            .Distinct()
            .ToArray();
    }
}
