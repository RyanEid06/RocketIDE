using RocketIDE.Rocket.LanguageServer.Features;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.App.Integration;

public interface IRocketEditorFeatureService
{
    IReadOnlyList<string> CompletionTriggerCharacters { get; }
    IReadOnlyList<string> SignatureTriggerCharacters { get; }
    IReadOnlyList<string> SignatureRetriggerCharacters { get; }

    Task<RocketCompletionResult?> RequestCompletionAsync(
        string path,
        LspPosition position,
        string? triggerCharacter,
        CancellationToken cancellationToken);

    Task<RocketHover?> RequestHoverAsync(
        string path,
        LspPosition position,
        CancellationToken cancellationToken);

    Task<RocketSignatureHelp?> RequestSignatureHelpAsync(
        string path,
        LspPosition position,
        string? triggerCharacter,
        bool isRetrigger,
        CancellationToken cancellationToken);

    Task<RocketSemanticTokensResult?> RequestSemanticTokensAsync(
        string path,
        CancellationToken cancellationToken);

    void InvalidateSemanticTokens(string path);
}
