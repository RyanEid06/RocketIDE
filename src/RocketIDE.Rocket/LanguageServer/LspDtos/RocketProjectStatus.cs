namespace RocketIDE.Rocket.LanguageServer.LspDtos;

public sealed record RocketProjectStatus(
    long Bytes,
    long ElapsedMilliseconds,
    int Files,
    long Generation,
    long MaximumProjectBytes,
    int MaximumProjectFiles,
    int Symbols);
