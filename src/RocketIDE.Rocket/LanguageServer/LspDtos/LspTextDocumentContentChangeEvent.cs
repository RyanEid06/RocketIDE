namespace RocketIDE.Rocket.LanguageServer.LspDtos;

public sealed record LspTextDocumentContentChangeEvent(LspRange? Range, string Text);
