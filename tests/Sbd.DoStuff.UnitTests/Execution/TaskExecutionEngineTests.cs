using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.UnitTests.Fakes;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Execution;

public class TaskExecutionEngineTests
{
    private static TaskExecutionEngine CreateEngine() => new(new FakeProcessRunner(), new InMemoryTaskRunStore());

    [Fact]
    public async Task SuccessfulTask_TransitionsThroughRunningToSucceeded()
    {
        var engine = CreateEngine();
        var gate = new TaskCompletionSource();
        var task = new FakeTask(run: async (_, _) => await gate.Task);

        var run = engine.StartRun(task);
        await WaitUntil(() => run.Status == TaskRunStatus.Running);
        gate.SetResult();
        await WaitUntil(() => run.Status == TaskRunStatus.Succeeded);

        run.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ThrowingTask_TransitionsToFailed()
    {
        var engine = CreateEngine();
        var task = new FakeTask(run: (_, _) => throw new InvalidOperationException("boom"));

        var run = engine.StartRun(task);
        await WaitUntil(() => run.Status == TaskRunStatus.Failed);

        run.ErrorMessage.ShouldBe("boom");
    }

    [Fact]
    public async Task CancelledTask_TransitionsToCancelled()
    {
        var engine = CreateEngine();
        var started = new TaskCompletionSource();
        var task = new FakeTask(run: async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        var run = engine.StartRun(task);
        await started.Task;
        engine.TryCancelRun(run.RunId).ShouldBeTrue();
        await WaitUntil(() => run.Status == TaskRunStatus.Cancelled);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }
}
