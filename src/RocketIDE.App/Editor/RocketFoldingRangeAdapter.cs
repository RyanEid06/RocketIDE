using System.IO;
using RocketIDE.App.Integration;
using RocketIDE.App.ViewModels;

namespace RocketIDE.App.Editor;

/// <summary>Connects one editor view to authoritative, versioned LSP folding ranges.</summary>
public sealed class RocketFoldingRangeAdapter(
    IRocketFoldingRangeProvider provider,
    Func<DocumentTabViewModel?> documentProvider) : IEditorFoldingRangeProvider
{
    public async Task<IReadOnlyList<EditorFoldingRange>?> RequestRangesAsync(
        string path,
        int documentVersion,
        CancellationToken cancellationToken)
    {
        var document = documentProvider();
        if (document is null || document.Version != documentVersion ||
            !string.Equals(Path.GetFullPath(document.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var snapshot = await provider.GetRangesAsync(document, cancellationToken);
        if (snapshot is null || document.Version != documentVersion ||
            snapshot.Version != documentVersion ||
            !string.Equals(snapshot.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return snapshot.Ranges
            .Select(range => new EditorFoldingRange(range.StartLine, range.EndLine))
            .ToArray();
    }
}
