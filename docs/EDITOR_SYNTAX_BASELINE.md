# Rocket editor syntax baseline

RocketIDE's pre-LSP syntax highlighting is intentionally lexical only.

The authoritative source grammar for this phase is the Rocket VS Code grammar snapshot at:

- `references/rocket-current/editors/vscode/syntaxes/rocket.tmLanguage.json`
- `references/rocket-current/editors/vscode/language-configuration.json`

AvalonEdit does not consume TextMate JSON directly, so IDE-WP04 ports the same lexical categories into an AvalonEdit XSHD definition owned by `RocketSyntaxHighlighting`. The port covers comments, quoted strings, escapes, numeric literals, declarations, control/storage keywords, primitive/common Rocket types, boolean constants, capitalized type-like identifiers, operators, and punctuation.

This layer must never grow into a Rocket parser or semantic analyzer. It may color and locally assist typing, but it must not decide whether code is valid, resolve symbols, infer types, or manufacture diagnostics. Those behaviors remain owned by `rocket-lsp.exe` in later work packages.

Editor-local ergonomics apply only to `.rocket` documents and follow Rocket's current language configuration:

- indentation is four spaces;
- tabs typed by the user are converted to spaces;
- a line ending in `:` increases the next-line indentation by four spaces;
- `else:` and `case ...:` are recognized as dedent-oriented lexical forms; when the line is still at the preceding body indent, pressing Enter safely pulls that line back by one four-space level before indenting its body;
- `()`, `[]`, double quotes, and single quotes are auto-close/surround pairs;
- quote auto-closing is suppressed inside an existing string or comment;
- lexical highlighting remains available even when the Rocket language server is absent.

When the upstream TextMate grammar changes, update this adapter deliberately and keep the reference snapshot synchronized. Do not silently invent new syntax here.

## Matching-bracket visualization

WP04 intentionally does not add a custom AvalonEdit bracket background renderer. Auto-close/surround behavior is implemented from Rocket's authoritative language configuration, but a visual bracket matcher is deferred to IDE-WP16 where it can be tested against the mature editor and DPI/theme matrix. Shipping an unverified renderer now would add editor complexity for cosmetic value and could highlight brackets incorrectly around quoted/commented text.
