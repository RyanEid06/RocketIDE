namespace RocketIDE.Core.Documents;

public enum DocumentSaveStatus
{
    Saved,
    NoChanges,
    Conflict,
}

public sealed record DocumentSaveResult(
    DocumentSaveStatus Status,
    DocumentSnapshot Document,
    string? Message = null);
