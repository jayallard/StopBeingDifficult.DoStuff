using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Processes;

namespace Sbd.DoStuff.UnitTests.Fakes;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public ProcessRunResult Result { get; set; } = new(0, WasCancelled: false);
    public IReadOnlyList<TaskOutputLine> LinesToEmit { get; set; } = [];

    public Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request, Action<TaskOutputLine> onOutputLine, CancellationToken cancellationToken)
    {
        foreach (var line in LinesToEmit)
        {
            onOutputLine(line);
        }

        return Task.FromResult(Result);
    }
}
