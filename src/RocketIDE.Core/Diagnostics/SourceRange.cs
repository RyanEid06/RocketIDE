namespace RocketIDE.Core.Diagnostics;

public sealed record SourceRange(int StartLine, int StartCharacter, int EndLine, int EndCharacter);
