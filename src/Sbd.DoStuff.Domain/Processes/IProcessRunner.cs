using Sbd.DoStuff.Domain.Execution;

namespace Sbd.DoStuff.Domain.Processes;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        Action<TaskOutputLine> onOutputLine,
        CancellationToken cancellationToken);
}
