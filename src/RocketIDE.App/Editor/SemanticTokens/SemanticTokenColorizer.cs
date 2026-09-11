using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace RocketIDE.App.Editor.SemanticTokens;

internal sealed class SemanticTokenColorizer(
    SemanticTokenMarkerCollection markers,
    Func<string, Brush?> brushResolver) : DocumentColorizingTransformer
{
    private readonly SemanticTokenMarkerCollection _markers = markers ?? throw new ArgumentNullException(nameof(markers));
    private readonly Func<string, Brush?> _brushResolver = brushResolver ?? throw new ArgumentNullException(nameof(brushResolver));

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var marker in _markers.Markers)
        {
            if (marker.EndOffset <= line.Offset)
            {
                continue;
            }
            if (marker.Offset >= line.EndOffset)
            {
                break;
            }

            var start = Math.Max(marker.Offset, line.Offset);
            var end = Math.Min(marker.EndOffset, line.EndOffset);
            var brush = ResolveBrush(marker.TokenType);
            if (brush is null || end <= start)
            {
                continue;
            }

            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private Brush? ResolveBrush(string tokenType)
    {
        var resourceKey = tokenType switch
        {
            "namespace" => "IDE.Semantic.NamespaceBrush",
            "type" or "class" or "enum" or "interface" or "struct" or "typeParameter" => "IDE.Semantic.TypeBrush",
            "function" or "method" => "IDE.Semantic.FunctionBrush",
            "parameter" => "IDE.Semantic.ParameterBrush",
            "property" or "field" => "IDE.Semantic.PropertyBrush",
            "enumMember" => "IDE.Semantic.EnumMemberBrush",
            "keyword" or "modifier" => "IDE.Semantic.KeywordBrush",
            "variable" => "IDE.Semantic.VariableBrush",
            _ => null,
        };
        return resourceKey is null ? null : _brushResolver(resourceKey);
    }
}
