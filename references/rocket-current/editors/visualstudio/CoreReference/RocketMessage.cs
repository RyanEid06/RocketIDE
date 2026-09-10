namespace Rocket.VisualStudio.Core
{
    internal sealed class RocketMessage
    {
        internal string Reason { get; set; }
        internal string Level { get; set; }
        internal string Code { get; set; }
        internal string Message { get; set; }
        internal string File { get; set; }
        internal int Line { get; set; }
        internal int Column { get; set; }
        internal string Command { get; set; }
        internal bool Success { get; set; }
        internal string Artifact { get; set; }
        internal string Cache { get; set; }
        internal string Name { get; set; }
        internal string Status { get; set; }
        internal int ExitCode { get; set; }
        internal int Passed { get; set; }
        internal int Failed { get; set; }
        internal int ExpectedFailures { get; set; }
        internal int Selected { get; set; }
    }
}
