using Sbd.DoStuff.Domain.Execution;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Execution;

public class InMemoryTaskRunStoreTests
{
    private static TaskRun NewRun(string taskId = "task-a", DateTimeOffset? startedAt = null) => new()
    {
        RunId = Guid.NewGuid(),
        TaskId = taskId,
        TaskName = taskId,
        StartedAt = startedAt ?? DateTimeOffset.UtcNow,
    };

    [Fact]
    public void AddThenGet_ReturnsTheSameRun()
    {
        var store = new InMemoryTaskRunStore();
        var run = NewRun();

        store.Add(run);

        store.Get(run.RunId).ShouldBeSameAs(run);
    }

    [Fact]
    public void Get_UnknownRunId_ReturnsNull()
    {
        var store = new InMemoryTaskRunStore();

        store.Get(Guid.NewGuid()).ShouldBeNull();
    }

    [Fact]
    public void GetRecentForTask_FiltersByTaskId_MostRecentFirst()
    {
        var store = new InMemoryTaskRunStore();
        var older = NewRun("task-a", DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = NewRun("task-a", DateTimeOffset.UtcNow);
        var other = NewRun("task-b");
        store.Add(older);
        store.Add(newer);
        store.Add(other);

        var recent = store.GetRecentForTask("task-a");

        recent.ShouldBe([newer, older]);
    }

    [Fact]
    public void GetAll_ReturnsEveryRun()
    {
        var store = new InMemoryTaskRunStore();
        store.Add(NewRun());
        store.Add(NewRun());

        store.GetAll().Count.ShouldBe(2);
    }
}
