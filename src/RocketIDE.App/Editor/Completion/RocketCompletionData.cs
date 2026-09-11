using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Editor.Completion;

internal sealed class RocketCompletionData(
    RocketCompletionItem item,
    int fallbackStartOffset,
    int requestVersion) : ICompletionData
{
    private readonly RocketCompletionItem _item = item ?? throw new ArgumentNullException(nameof(item));
    private readonly int _fallbackStartOffset = fallbackStartOffset;

    public int RequestVersion { get; } = requestVersion;
    public ImageSource? Image => null;
    public string Text => _item.FilterText ?? _item.Label;
    public object Content => BuildContent();
    public object Description => BuildDescription();
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        var caretOffset = textArea.Caret.Offset;
        var start = Math.Clamp(_fallbackStartOffset, 0, textArea.Document.TextLength);
        var length = Math.Max(0, caretOffset - start);
        if (!CompletionEditApplier.TryApply(textArea.Document, _item, start, length, out var error))
        {
            Trace.TraceWarning($"Rocket completion edit rejected: {error}");
        }
    }

    private object BuildContent()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = _item.Label,
            Foreground = Brushes.WhiteSmoke,
        });
        if (_item.Kind is int kind)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"  {CompletionKindName(kind)}",
                Foreground = Brushes.DarkGray,
                FontSize = 11,
            });
        }
        return panel;
    }

    private object BuildDescription()
    {
        var panel = new StackPanel { MaxWidth = 520 };
        if (!string.IsNullOrWhiteSpace(_item.Detail))
        {
            panel.Children.Add(new TextBlock
            {
                Text = _item.Detail,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        if (_item.Documentation is { Value.Length: > 0 } documentation)
        {
            panel.Children.Add(new TextBlock
            {
                Text = documentation.Value,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        return panel;
    }

    private static string CompletionKindName(int kind) => kind switch
    {
        1 => "text",
        2 => "method",
        3 => "function",
        4 => "constructor",
        5 => "field",
        6 => "variable",
        7 => "class",
        8 => "interface",
        9 => "module",
        10 => "property",
        13 => "enum",
        14 => "keyword",
        21 => "constant",
        22 => "struct",
        25 => "type parameter",
        _ => "symbol",
    };
}
