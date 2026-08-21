namespace Sbd.DoStuff.Domain.Execution;

public interface ITaskRunStore
{
    void Add(TaskRun run);
    TaskRun? Get(Guid runId);
    IReadOnlyList<TaskRun> GetRecentForTask(string listId, string taskId, int limit = 20);
    IReadOnlyList<TaskRun> GetAll();
    void ClearForList(string listId);
}
