namespace RocketIDE.Core.Debugger;

public sealed record DebuggerFeasibility(
    string Backend,
    IReadOnlySet<string> Capabilities,
    bool Redistributable,
    bool PrototypeEvidenceAvailable,
    IReadOnlyList<string> Limitations,
    string Recommendation)
{
    public bool IsReadyToImplement => Redistributable && PrototypeEvidenceAvailable;

    public static DebuggerFeasibility Deferred(string reason) => new(
        "Visual Studio native engine (reference only)",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CodeView/PDB source breakpoints",
            "native stepping",
            "native call frames",
            "locals where represented",
        },
        Redistributable: false,
        PrototypeEvidenceAvailable: false,
        [reason, "No standalone DAP/debug adapter contract is present in RocketIDE."],
        "DEFERRED: validate a redistributable Rocket DAP/debug adapter before implementing a debugger.");
}
