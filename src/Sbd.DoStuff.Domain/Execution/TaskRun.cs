using System.Collections.Concurrent;

namespace Sbd.DoStuff.Domain.Execution;

/// <summary>
/// Mutable — updated in place while a run is in progress and observed directly
/// by the UI via ITaskExecutionEngine.RunChanged, rather than as a stream of
/// immutable snapshots.
/// </summary>
public sealed class TaskRun
{
    private readonly ConcurrentQueue<TaskOutputLine> _outputLines = new();

    public required Guid RunId { get; init; }
    public required string ListId { get; init; }
    public required string TaskId { get; init; }
    public required string TaskName { get; init; }
    public TaskRunStatus Status { get; internal set; } = TaskRunStatus.Pending;
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; internal set; }
    public string? ErrorMessage { get; internal set; }
    public int? ResultCode { get; internal set; }
    public string? ResultMessage { get; internal set; }

    public IReadOnlyList<TaskOutputLine> OutputLines => _outputLines.ToArray();

    internal void AppendOutputLine(TaskOutputLine line) => _outputLines.Enqueue(line);
}
