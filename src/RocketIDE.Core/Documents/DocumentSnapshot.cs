namespace RocketIDE.Core.Documents;

public sealed record DocumentSnapshot(
    DocumentId Id,
    string Path,
    string Text,
    int Version,
    bool IsDirty,
    long ByteLength);
