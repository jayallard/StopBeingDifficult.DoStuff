using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.UnitTests.Fakes;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Execution;

public class TaskExecutionContextTests
{
    private static TaskRun NewRun() => new()
    {
        RunId = Guid.NewGuid(),
        TaskId = "task-a",
        TaskName = "Task A",
        StartedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Report_AppendsLine_AndRaisesChanged()
    {
        var run = NewRun();
        var changeCount = 0;
        var context = new TaskExecutionContext(run, new FakeProcessRunner(), _ => changeCount++);

        context.Report(new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.StandardOutput, "hello"));

        run.OutputLines.ShouldHaveSingleItem();
        changeCount.ShouldBe(1);
    }

    [Fact]
    public void Log_AppendsSystemLine_AndRaisesChanged()
    {
        var run = NewRun();
        var changeCount = 0;
        var context = new TaskExecutionContext(run, new FakeProcessRunner(), _ => changeCount++);

        context.Log("a log message");

        run.OutputLines.ShouldHaveSingleItem();
        run.OutputLines[0].Stream.ShouldBe(OutputStream.System);
        changeCount.ShouldBe(1);
    }

    [Fact]
    public void SetResult_SetsFields_AppendsLine_AndCanBeOverwritten()
    {
        var run = NewRun();
        var changeCount = 0;
        var context = new TaskExecutionContext(run, new FakeProcessRunner(), _ => changeCount++);

        context.SetResult(0, "deleted");

        run.ResultCode.ShouldBe(0);
        run.ResultMessage.ShouldBe("deleted");
        changeCount.ShouldBe(1);

        context.SetResult(-1, "folder doesn't exist");

        run.ResultCode.ShouldBe(-1);
        run.ResultMessage.ShouldBe("folder doesn't exist");
        changeCount.ShouldBe(2);
    }

    [Fact]
    public void ReportLogSetResult_PreserveCallOrder()
    {
        var run = NewRun();
        var context = new TaskExecutionContext(run, new FakeProcessRunner(), _ => { });

        context.Report(new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.StandardOutput, "1"));
        context.Log("2");
        context.SetResult(0, "3");

        run.OutputLines.Select(l => l.Text).ShouldBe(["1", "2", "Result: 0 — 3"]);
    }
}
