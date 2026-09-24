using RocketIDE.App.Editor;
using RocketIDE.Core.Diagnostics;
using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Integration;

public sealed class ValidatedEditorNavigation : IEditorNavigation
{
    private readonly IEditorNavigation _inner;
    private readonly Func<string, CancellationToken, Task<string?>> _textProvider;
    private readonly Action<string>? _reportRejected;

    public ValidatedEditorNavigation(
        IEditorNavigation inner,
        Func<string, CancellationToken, Task<string?>> textProvider,
        Action<string>? reportRejected = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _textProvider = textProvider ?? throw new ArgumentNullException(nameof(textProvider));
        _reportRejected = reportRejected;
    }

    public async Task<IEditorViewContext?> OpenOrRevealAsync(
        string path,
        SourceRange? range = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (range is not null)
        {
            var text = await _textProvider(path, cancellationToken).ConfigureAwait(false);
            if (text is null || !WorkspaceEditValidator.IsValidRange(
                    text,
                    new RocketLocation(
                        path,
                        new LspRange(
                            new LspPosition(range.StartLine, range.StartCharacter),
                            new LspPosition(range.EndLine, range.EndCharacter)))))
            {
                _reportRejected?.Invoke($"Rejected navigation target with invalid UTF-16 range: {path}");
                return null;
            }
        }
        return await _inner.OpenOrRevealAsync(path, range, cancellationToken).ConfigureAwait(false);
    }
}
