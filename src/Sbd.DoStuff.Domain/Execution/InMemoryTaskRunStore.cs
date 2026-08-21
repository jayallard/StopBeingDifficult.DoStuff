using System.Collections.Concurrent;

namespace Sbd.DoStuff.Domain.Execution;

internal sealed class InMemoryTaskRunStore : ITaskRunStore
{
    private readonly ConcurrentDictionary<Guid, TaskRun> _runs = new();

    public void Add(TaskRun run) => _runs[run.RunId] = run;

    public TaskRun? Get(Guid runId) => _runs.GetValueOrDefault(runId);

    public IReadOnlyList<TaskRun> GetRecentForTask(string taskId, int limit = 20) =>
        _runs.Values
            .Where(r => r.TaskId == taskId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToList();

    public IReadOnlyList<TaskRun> GetAll() => _runs.Values.OrderByDescending(r => r.StartedAt).ToList();

    public void ClearForTasks(IEnumerable<string> taskIds)
    {
        var ids = new HashSet<string>(taskIds);
        foreach (var run in _runs.Values.Where(r => ids.Contains(r.TaskId)))
        {
            _runs.TryRemove(run.RunId, out _);
        }
    }
}
