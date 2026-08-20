using Sbd.DoStuff.Domain.Processes;

namespace Sbd.DoStuff.Domain.Execution;

internal sealed class TaskExecutionContext(TaskRun run, IProcessRunner processRunner, Action<TaskRun> onChanged)
    : Tasks.ITaskExecutionContext
{
    public IProcessRunner ProcessRunner { get; } = processRunner;

    public void Report(TaskOutputLine line) => Append(line);

    public void Log(string message) =>
        Append(new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.System, message));

    public void SetResult(int resultCode, string message)
    {
        run.ResultCode = resultCode;
        run.ResultMessage = message;
        Append(new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.System, $"Result: {resultCode} — {message}"));
    }

    private void Append(TaskOutputLine line)
    {
        run.AppendOutputLine(line);
        onChanged(run);
    }
}
