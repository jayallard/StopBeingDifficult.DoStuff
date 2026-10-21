using Sbd.DoStuff.Domain.Tasks;

namespace Sbd.DoStuff.Domain.Execution;

public interface ITaskExecutionEngine
{
    TaskRun StartRun(ITask task, string listId, string? entryKey = null);
    bool TryCancelRun(Guid runId);

    /// <summary>Cancels every active run (killing its process tree) and waits up to <paramref name="timeout"/> for them to finish.</summary>
    Task CancelAllRunsAsync(TimeSpan timeout);

    event Action<TaskRun>? RunChanged;
}
