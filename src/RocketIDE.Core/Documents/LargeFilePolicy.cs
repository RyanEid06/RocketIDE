namespace RocketIDE.Core.Documents;

public static class LargeFilePolicy
{
    public const long MaxLspDocumentBytes = 4L * 1024 * 1024;
    public const long MaxSyntaxColoringBytes = 16L * 1024 * 1024;
    public const long MaxEditorBufferBytes = 64L * 1024 * 1024;

    public static LargeFileDecision Decide(long byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
        var isLargeFileMode = byteLength > MaxLspDocumentBytes;
        return new LargeFileDecision(
            byteLength,
            MaxLspDocumentBytes,
            isLargeFileMode,
            AllowLsp: !isLargeFileMode,
            AllowLocalEditing: byteLength <= MaxEditorBufferBytes,
            AllowFindAndGoto: byteLength <= MaxEditorBufferBytes,
            AllowSyntaxColoring: byteLength <= MaxSyntaxColoringBytes,
            Reason: byteLength > MaxEditorBufferBytes
                ? "Large File Mode: local editing, find, and goto are disabled above the 64 MiB UTF-8 editor safety limit; saving the current buffer remains available."
                : isLargeFileMode
                    ? "Large File Mode: Rocket LSP is disabled above the 4 MiB UTF-8 document limit; local editing, find, goto, and save remain available."
                    : "Rocket LSP semantics are enabled for this document.");
    }
}
