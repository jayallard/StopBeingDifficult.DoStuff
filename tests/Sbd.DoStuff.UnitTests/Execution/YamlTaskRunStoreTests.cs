using Sbd.DoStuff.Domain.Execution;
using Shouldly;

namespace Sbd.DoStuff.UnitTests.Execution;

public class YamlTaskRunStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dostuff-taskruns-").FullName;
    private readonly string _filePath;

    public YamlTaskRunStoreTests() => _filePath = Path.Combine(_directory, "TaskRuns.yaml");

    private static TaskRun NewRun(string taskId = "task-a", string listId = "list-a", DateTimeOffset? startedAt = null) => new()
    {
        RunId = Guid.NewGuid(),
        ListId = listId,
        TaskId = taskId,
        TaskName = taskId,
        StartedAt = startedAt ?? DateTimeOffset.UtcNow,
    };

    [Fact]
    public void AddThenGet_ReturnsARunWithTheSameValues()
    {
        var store = new YamlTaskRunStore(_filePath);
        var run = NewRun();

        store.Add(run);

        store.Get(run.RunId).ShouldBeSameAs(run);
    }

    [Fact]
    public void Get_UnknownRunId_ReturnsNull()
    {
        var store = new YamlTaskRunStore(_filePath);

        store.Get(Guid.NewGuid()).ShouldBeNull();
    }

    [Fact]
    public void GetRecentForTask_FiltersByListAndTaskId_MostRecentFirst()
    {
        var store = new YamlTaskRunStore(_filePath);
        var older = NewRun("task-a", startedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = NewRun("task-a", startedAt: DateTimeOffset.UtcNow);
        var other = NewRun("task-b");
        store.Add(older);
        store.Add(newer);
        store.Add(other);

        store.GetRecentForTask("list-a", "task-a").ShouldBe([newer, older]);
    }

    [Fact]
    public void ClearForList_RemovesOnlyMatchingListId()
    {
        var store = new YamlTaskRunStore(_filePath);
        var runA = NewRun("task-a", "list-a");
        var runB = NewRun("task-a", "list-b");
        store.Add(runA);
        store.Add(runB);

        store.ClearForList("list-a");

        store.GetAll().ShouldBe([runB]);
    }

    [Fact]
    public void Add_WritesFileToDisk()
    {
        var store = new YamlTaskRunStore(_filePath);

        store.Add(NewRun());

        File.Exists(_filePath).ShouldBeTrue();
    }

    [Fact]
    public void Constructor_MissingFile_StartsEmpty()
    {
        var store = new YamlTaskRunStore(_filePath);

        store.GetAll().ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_LoadsRunsPersistedByAnEarlierStore()
    {
        var run = NewRun("task-a", "list-a");
        var first = new YamlTaskRunStore(_filePath);
        first.Add(run);

        var second = new YamlTaskRunStore(_filePath);

        var loaded = second.Get(run.RunId);
        loaded.ShouldNotBeNull();
        loaded.RunId.ShouldBe(run.RunId);
        loaded.ListId.ShouldBe(run.ListId);
        loaded.TaskId.ShouldBe(run.TaskId);
        loaded.TaskName.ShouldBe(run.TaskName);
        loaded.StartedAt.ShouldBe(run.StartedAt);
    }

    [Fact]
    public void Constructor_LoadsMutatedStateCapturedByALaterSnapshot()
    {
        var run = NewRun();
        var first = new YamlTaskRunStore(_filePath);
        first.Add(run);

        run.Status = TaskRunStatus.Succeeded;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.ResultCode = 0;
        run.ResultMessage = "done";
        run.AppendOutputLine(new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.StandardOutput, "hello"));

        first.Add(NewRun("task-b"));

        var second = new YamlTaskRunStore(_filePath);

        var loaded = second.Get(run.RunId);
        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe(TaskRunStatus.Succeeded);
        loaded.ResultCode.ShouldBe(0);
        loaded.ResultMessage.ShouldBe("done");
        loaded.OutputLines.Count.ShouldBe(1);
        loaded.OutputLines[0].Text.ShouldBe("hello");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
