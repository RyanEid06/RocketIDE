namespace RocketIDE.Core.Documents;

public interface IDocumentStore
{
    IReadOnlyList<DocumentSnapshot> OpenDocuments { get; }

    Task<DocumentSnapshot> OpenAsync(string path, CancellationToken cancellationToken);

    DocumentSnapshot UpdateText(DocumentId id, string text);

    Task<DocumentSaveResult> SaveAsync(
        DocumentId id,
        bool overwriteExternalChanges,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentSaveResult>> SaveAllAsync(CancellationToken cancellationToken);

    bool TryGet(DocumentId id, out DocumentSnapshot? document);

    bool Close(DocumentId id);
}
