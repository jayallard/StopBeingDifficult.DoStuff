using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Processes;
using Sbd.DoStuff.Domain.Tasks;

namespace Sbd.DoStuff.UnitTests.Fakes;

internal sealed class FakeTaskExecutionContext(IProcessRunner processRunner) : ITaskExecutionContext
{
    public List<TaskOutputLine> Reported { get; } = [];
    public int? ResultCode { get; private set; }
    public string? ResultMessage { get; private set; }

    public IProcessRunner ProcessRunner { get; } = processRunner;

    public void Report(TaskOutputLine line) => Reported.Add(line);

    public void Log(string message) => Reported.Add(new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.System, message));

    public void SetResult(int resultCode, string message)
    {
        ResultCode = resultCode;
        ResultMessage = message;
    }
}
