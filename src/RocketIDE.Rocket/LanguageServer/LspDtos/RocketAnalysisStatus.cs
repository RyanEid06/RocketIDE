namespace RocketIDE.Rocket.LanguageServer.LspDtos;

public sealed record RocketAnalysisStatus(
    long Bytes,
    long ElapsedMilliseconds,
    int Files,
    long Generation,
    int InvalidatedFiles);
