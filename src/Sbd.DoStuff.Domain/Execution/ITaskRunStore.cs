namespace Sbd.DoStuff.Domain.Execution;

public interface ITaskRunStore
{
    void Add(TaskRun run);
    TaskRun? Get(Guid runId);
    IReadOnlyList<TaskRun> GetRecentForTask(string taskId, int limit = 20);
    IReadOnlyList<TaskRun> GetAll();
    void ClearForTasks(IEnumerable<string> taskIds);
}
