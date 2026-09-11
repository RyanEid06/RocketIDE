using ICSharpCode.AvalonEdit.Document;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Editor.SemanticTokens;

public sealed record SemanticTokenMarker(int Offset, int Length, string TokenType, IReadOnlyList<string> Modifiers)
{
    public int EndOffset => Offset + Length;
}

public sealed class SemanticTokenMarkerCollection
{
    private IReadOnlyList<SemanticTokenMarker> _markers = [];

    public IReadOnlyList<SemanticTokenMarker> Markers => _markers;

    public void Update(TextDocument document, IReadOnlyList<RocketSemanticToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(tokens);
        var markers = new List<SemanticTokenMarker>(tokens.Count);
        foreach (var token in tokens)
        {
            if (token.Length <= 0 ||
                !LspTextRangeMapper.TryGetOffset(document, token.Line, token.Character, out var start) ||
                !LspTextRangeMapper.TryGetOffset(document, token.Line, token.Character + token.Length, out var end) ||
                end <= start)
            {
                continue;
            }
            markers.Add(new SemanticTokenMarker(start, end - start, token.TokenType, token.Modifiers));
        }
        _markers = markers.OrderBy(marker => marker.Offset).ToArray();
    }

    public void Clear() => _markers = [];
}
