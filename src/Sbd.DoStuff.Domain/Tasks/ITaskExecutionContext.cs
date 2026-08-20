using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Processes;

namespace Sbd.DoStuff.Domain.Tasks;

/// <summary>
/// Real-time feedback channel a running task uses to report back to the app.
/// Report/Log/SetResult all flow through the same append+notify pipe on the
/// owning TaskRun, so they interleave in timestamp order in one live view.
/// </summary>
public interface ITaskExecutionContext
{
    IProcessRunner ProcessRunner { get; }

    void Report(TaskOutputLine line);
    void Log(string message);
    void SetResult(int resultCode, string message);
}
