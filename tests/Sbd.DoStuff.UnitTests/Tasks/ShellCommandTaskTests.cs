using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Processes;
using Sbd.DoStuff.Domain.Tasks;
using Sbd.DoStuff.UnitTests.Fakes;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Tasks;

public class ShellCommandTaskTests
{
    [Fact]
    public async Task NonZeroExitCode_ThrowsTaskExecutionFailedException()
    {
        var runner = new FakeProcessRunner { Result = new ProcessRunResult(1, WasCancelled: false) };
        var task = new ShellCommandTask("id", "Name", "command", null, null, null);
        var context = new FakeTaskExecutionContext(runner);

        await Should.ThrowAsync<TaskExecutionFailedException>(() => task.RunAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task OutputLines_FromRunner_AreForwardedToContext_WithCorrectStream()
    {
        var runner = new FakeProcessRunner
        {
            LinesToEmit =
            [
                new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.StandardOutput, "stdout line"),
                new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.StandardError, "stderr line"),
            ],
        };
        var task = new ShellCommandTask("id", "Name", "command", null, null, null);
        var context = new FakeTaskExecutionContext(runner);

        await task.RunAsync(context, CancellationToken.None);

        context.Reported.ShouldContain(l => l.Stream == OutputStream.StandardOutput && l.Text == "stdout line");
        context.Reported.ShouldContain(l => l.Stream == OutputStream.StandardError && l.Text == "stderr line");
    }

    [Fact]
    public async Task Completion_SetsResult_FromProcessExitCode()
    {
        var runner = new FakeProcessRunner { Result = new ProcessRunResult(0, WasCancelled: false) };
        var task = new ShellCommandTask("id", "Name", "command", null, null, null);
        var context = new FakeTaskExecutionContext(runner);

        await task.RunAsync(context, CancellationToken.None);

        context.ResultCode.ShouldBe(0);
    }
}
