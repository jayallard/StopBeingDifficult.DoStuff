using System.Collections.Concurrent;
using Sbd.DoStuff.Domain.Processes;
using Sbd.DoStuff.Domain.Tasks;

namespace Sbd.DoStuff.Domain.Execution;

internal sealed class TaskExecutionEngine(IProcessRunner processRunner, ITaskRunStore runStore) : ITaskExecutionEngine
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationSources = new();

    public event Action<TaskRun>? RunChanged;

    public TaskRun StartRun(ITask task)
    {
        var run = new TaskRun
        {
            RunId = Guid.NewGuid(),
            TaskId = task.Id,
            TaskName = task.Name,
            StartedAt = DateTimeOffset.UtcNow,
        };
        runStore.Add(run);

        var cts = new CancellationTokenSource();
        _cancellationSources[run.RunId] = cts;

        _ = Task.Run(() => ExecuteAsync(task, run, cts.Token));

        return run;
    }

    public bool TryCancelRun(Guid runId)
    {
        if (_cancellationSources.TryGetValue(runId, out var cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }

    private async Task ExecuteAsync(ITask task, TaskRun run, CancellationToken cancellationToken)
    {
        run.Status = TaskRunStatus.Running;
        RunChanged?.Invoke(run);

        var context = new TaskExecutionContext(run, processRunner, r => RunChanged?.Invoke(r));

        try
        {
            await task.RunAsync(context, cancellationToken);
            run.Status = TaskRunStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            run.Status = TaskRunStatus.Cancelled;
        }
        catch (Exception ex)
        {
            run.ErrorMessage = ex.Message;
            context.Log($"Failed: {ex.Message}");
            run.Status = TaskRunStatus.Failed;
        }
        finally
        {
            run.CompletedAt = DateTimeOffset.UtcNow;
            _cancellationSources.TryRemove(run.RunId, out _);
            RunChanged?.Invoke(run);
        }
    }
}
