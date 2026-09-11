using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RocketIDE.App.Editor.Hover;
using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Editor.SignatureHelp;

internal static class SignatureHelpPresenter
{
    public static FrameworkElement Create(RocketSignatureHelp help)
    {
        ArgumentNullException.ThrowIfNull(help);
        var signature = help.Signatures[Math.Clamp(help.ActiveSignature, 0, help.Signatures.Count - 1)];
        var panel = new StackPanel { MaxWidth = 640 };
        var label = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
        };
        var activeRange = SignatureHelpFormatter.GetActiveParameterRange(help);
        if (activeRange is { } range)
        {
            label.Inlines.Add(new Run(signature.Label[..range.Start]));
            label.Inlines.Add(new Run(signature.Label.Substring(range.Start, range.Length)) { FontWeight = FontWeights.Bold });
            label.Inlines.Add(new Run(signature.Label[(range.Start + range.Length)..]));
        }
        else
        {
            label.Text = signature.Label;
        }
        panel.Children.Add(label);

        if (help.ActiveParameter >= 0 && help.ActiveParameter < signature.Parameters.Count &&
            signature.Parameters[help.ActiveParameter].Documentation is { Value.Length: > 0 } parameterDocumentation)
        {
            panel.Children.Add(CreateDocumentation(parameterDocumentation));
        }
        else if (signature.Documentation is { Value.Length: > 0 } signatureDocumentation)
        {
            panel.Children.Add(CreateDocumentation(signatureDocumentation));
        }

        return panel;
    }

    private static FrameworkElement CreateDocumentation(RocketMarkupContent content)
    {
        var element = SafeMarkdownPresenter.Create(content);
        element.Margin = new Thickness(0, 5, 0, 0);
        return element;
    }
}
