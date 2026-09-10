namespace RocketIDE.Rocket.LanguageServer.LspDtos;

public sealed record TextDocumentIdentifier(string Uri);
public sealed record VersionedTextDocumentIdentifier(string Uri, int Version);
public sealed record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);
public sealed record DidOpenTextDocumentParams(TextDocumentItem TextDocument);
public sealed record DidChangeTextDocumentParams(VersionedTextDocumentIdentifier TextDocument, IReadOnlyList<LspTextDocumentContentChangeEvent> ContentChanges);
public sealed record DidSaveTextDocumentParams(TextDocumentIdentifier TextDocument, string? Text);
public sealed record DidCloseTextDocumentParams(TextDocumentIdentifier TextDocument);
