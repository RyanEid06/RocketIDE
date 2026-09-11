using RocketIDE.Rocket.LanguageServer.Features;

namespace RocketIDE.App.Editor.SignatureHelp;

public static class SignatureHelpFormatter
{
    public static (int Start, int Length)? GetActiveParameterRange(RocketSignatureHelp help)
    {
        ArgumentNullException.ThrowIfNull(help);
        if (help.Signatures.Count == 0 || help.ActiveSignature < 0 || help.ActiveSignature >= help.Signatures.Count)
        {
            return null;
        }

        var signature = help.Signatures[help.ActiveSignature];
        var activeParameter = help.ActiveParameter;
        if (activeParameter < 0 || activeParameter >= signature.Parameters.Count)
        {
            return null;
        }

        var parameter = signature.Parameters[activeParameter];
        if (parameter.LabelStart is int start && parameter.LabelEnd is int end &&
            start >= 0 && end >= start && end <= signature.Label.Length)
        {
            return (start, end - start);
        }

        if (!string.IsNullOrEmpty(parameter.Label))
        {
            var match = signature.Label.IndexOf(parameter.Label, StringComparison.Ordinal);
            if (match >= 0)
            {
                return (match, parameter.Label.Length);
            }
        }

        return null;
    }
}
