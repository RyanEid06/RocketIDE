using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Editor.Hover;

internal static class SafeMarkdownPresenter
{
    public static FrameworkElement Create(RocketMarkupContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var textBlock = new TextBlock
        {
            MaxWidth = 560,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.WhiteSmoke,
        };

        var runs = string.Equals(content.Kind, "markdown", StringComparison.OrdinalIgnoreCase)
            ? SafeMarkdownParser.Parse(content.Value)
            : new[] { new HoverRun(HoverRunKind.Text, content.Value) };

        foreach (var run in runs)
        {
            switch (run.Kind)
            {
                case HoverRunKind.Code:
                    textBlock.Inlines.Add(new Run(run.Text)
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                    });
                    break;
                case HoverRunKind.Link when !string.IsNullOrWhiteSpace(run.Target):
                    var target = run.Target!;
                    var hyperlink = new Hyperlink(new Run($"{run.Text} ⧉"))
                    {
                        ToolTip = $"Copy link: {target}",
                    };
                    hyperlink.Click += (_, _) => TryCopy(target);
                    textBlock.Inlines.Add(hyperlink);
                    break;
                case HoverRunKind.LineBreak:
                    textBlock.Inlines.Add(new LineBreak());
                    break;
                default:
                    textBlock.Inlines.Add(new Run(run.Text));
                    break;
            }
        }

        return textBlock;
    }

    private static void TryCopy(string value)
    {
        try
        {
            Clipboard.SetText(value);
        }
        catch (ExternalException)
        {
            // Clipboard contention must not make a hover interaction fatal.
        }
    }
}
