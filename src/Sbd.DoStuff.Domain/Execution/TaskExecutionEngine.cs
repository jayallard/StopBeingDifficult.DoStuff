using System.Collections.Concurrent;
using Sbd.DoStuff.Domain.Processes;
using Sbd.DoStuff.Domain.Tasks;

namespace Sbd.DoStuff.Domain.Execution;

internal sealed class TaskExecutionEngine(IProcessRunner processRunner, ITaskRunStore runStore) : ITaskExecutionEngine
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationSources = new();

    public event Action<TaskRun>? RunChanged;

    public TaskRun StartRun(ITask task, string listId, string? entryKey = null)
    {
        var run = new TaskRun
        {
            RunId = Guid.NewGuid(),
            ListId = listId,
            EntryKey = entryKey,
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

    public async Task CancelAllRunsAsync(TimeSpan timeout)
    {
        foreach (var cts in _cancellationSources.Values)
        {
            cts.Cancel();
        }

        var deadline = DateTime.UtcNow + timeout;
        while (!_cancellationSources.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    private async Task ExecuteAsync(ITask task, TaskRun run, CancellationToken cancellationToken)
    {
        run.Status = TaskRunStatus.Running;
        runStore.Add(run);
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
            runStore.Add(run);
            RunChanged?.Invoke(run);
        }
    }
}
