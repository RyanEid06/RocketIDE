using RocketIDE.Core.Output;

namespace RocketIDE.Core.Commands;

public sealed record ProcessRunResult(int ExitCode, bool Cancelled);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessStartRequest request,
        IProgress<ProcessOutput> output,
        CancellationToken cancellationToken);

    void StopActive();
}
