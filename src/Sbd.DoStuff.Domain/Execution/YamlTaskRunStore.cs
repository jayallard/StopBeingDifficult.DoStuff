using System.Collections.Concurrent;
using Sbd.DoStuff.Domain.Serialization;
using YamlDotNet.Serialization;

namespace Sbd.DoStuff.Domain.Execution;

/// <summary>
/// Persists runs to a YAML file so they survive an app restart. Snapshots the full run
/// list to disk on Add/ClearForList — the only mutation points the ITaskRunStore
/// interface exposes. A run's Status/OutputLines/etc. mutate in place after Add() (see
/// TaskExecutionEngine), so a later snapshot still picks up those live values via the
/// shared in-memory reference; only the run added by the *current* call is guaranteed
/// fresh (Pending, no output) in that snapshot.
/// </summary>
internal sealed class YamlTaskRunStore : ITaskRunStore
{
    private static readonly IDeserializer Deserializer = YamlDeserializerFactory.Create();
    private static readonly ISerializer Serializer = YamlSerializerFactory.Create();

    private readonly string _filePath;
    private readonly ConcurrentDictionary<Guid, TaskRun> _runs = new();
    private readonly Lock _fileLock = new();

    public YamlTaskRunStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public void Add(TaskRun run)
    {
        _runs[run.RunId] = run;
        Save();
    }

    public TaskRun? Get(Guid runId) => _runs.GetValueOrDefault(runId);

    public IReadOnlyList<TaskRun> GetRecentForTask(string listId, string taskId, int limit = 20) =>
        _runs.Values
            .Where(r => r.ListId == listId && r.TaskId == taskId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToList();

    public IReadOnlyList<TaskRun> GetAll() => _runs.Values.OrderByDescending(r => r.StartedAt).ToList();

    public void ClearForList(string listId)
    {
        foreach (var run in _runs.Values.Where(r => r.ListId == listId))
        {
            _runs.TryRemove(run.RunId, out _);
        }

        Save();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var yaml = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return;
        }

        var records = Deserializer.Deserialize<List<TaskRunRecord>>(yaml) ?? [];
        foreach (var record in records)
        {
            _runs[record.RunId] = record.ToTaskRun();
        }
    }

    private void Save()
    {
        var records = _runs.Values
            .OrderBy(r => r.StartedAt)
            .Select(TaskRunRecord.FromTaskRun)
            .ToList();

        var yaml = Serializer.Serialize(records);

        lock (_fileLock)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, yaml);
        }
    }

    private sealed record TaskRunRecord
    {
        public required Guid RunId { get; init; }
        public required string ListId { get; init; }
        public required string TaskId { get; init; }
        public required string TaskName { get; init; }
        public TaskRunStatus Status { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public string? ErrorMessage { get; init; }
        public int? ResultCode { get; init; }
        public string? ResultMessage { get; init; }
        public IReadOnlyList<TaskOutputLine> OutputLines { get; init; } = [];

        public static TaskRunRecord FromTaskRun(TaskRun run) => new()
        {
            RunId = run.RunId,
            ListId = run.ListId,
            TaskId = run.TaskId,
            TaskName = run.TaskName,
            Status = run.Status,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            ErrorMessage = run.ErrorMessage,
            ResultCode = run.ResultCode,
            ResultMessage = run.ResultMessage,
            OutputLines = run.OutputLines,
        };

        public TaskRun ToTaskRun()
        {
            var run = new TaskRun
            {
                RunId = RunId,
                ListId = ListId,
                TaskId = TaskId,
                TaskName = TaskName,
                StartedAt = StartedAt,
                Status = Status,
                CompletedAt = CompletedAt,
                ErrorMessage = ErrorMessage,
                ResultCode = ResultCode,
                ResultMessage = ResultMessage,
            };

            foreach (var line in OutputLines)
            {
                run.AppendOutputLine(line);
            }

            return run;
        }
    }
}
