using Sbd.DoStuff.Domain.Tasks;

namespace Sbd.DoStuff.Domain.Execution;

public interface ITaskExecutionEngine
{
    TaskRun StartRun(ITask task, string listId, string? entryKey = null);
    bool TryCancelRun(Guid runId);

    event Action<TaskRun>? RunChanged;
}
