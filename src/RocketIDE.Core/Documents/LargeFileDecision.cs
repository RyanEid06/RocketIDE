namespace RocketIDE.Core.Documents;

public sealed record LargeFileDecision(
    long ByteLength,
    long LspLimitBytes,
    bool IsLargeFileMode,
    bool AllowLsp,
    bool AllowLocalEditing,
    bool AllowFindAndGoto,
    bool AllowSyntaxColoring,
    string Reason);
