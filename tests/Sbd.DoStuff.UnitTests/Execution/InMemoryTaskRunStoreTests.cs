using Sbd.DoStuff.Domain.Execution;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Execution;

public class InMemoryTaskRunStoreTests
{
    private static TaskRun NewRun(string taskId = "task-a", string listId = "list-a", DateTimeOffset? startedAt = null) => new()
    {
        RunId = Guid.NewGuid(),
        ListId = listId,
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
        var older = NewRun("task-a", startedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = NewRun("task-a", startedAt: DateTimeOffset.UtcNow);
        var other = NewRun("task-b");
        store.Add(older);
        store.Add(newer);
        store.Add(other);

        var recent = store.GetRecentForTask("list-a", "task-a");

        recent.ShouldBe([newer, older]);
    }

    [Fact]
    public void GetRecentForTask_FiltersByListId()
    {
        var store = new InMemoryTaskRunStore();
        var inListA = NewRun("task-a", "list-a");
        var inListB = NewRun("task-a", "list-b");
        store.Add(inListA);
        store.Add(inListB);

        store.GetRecentForTask("list-a", "task-a").ShouldBe([inListA]);
    }

    [Fact]
    public void GetAll_ReturnsEveryRun()
    {
        var store = new InMemoryTaskRunStore();
        store.Add(NewRun());
        store.Add(NewRun());

        store.GetAll().Count.ShouldBe(2);
    }

    [Fact]
    public void ClearForList_RemovesOnlyMatchingListId()
    {
        var store = new InMemoryTaskRunStore();
        var runA = NewRun("task-a", "list-a");
        var runB = NewRun("task-a", "list-b");
        store.Add(runA);
        store.Add(runB);

        store.ClearForList("list-a");

        store.GetAll().ShouldBe([runB]);
    }
}
