using Sbd.DoStuff.Domain.Execution;

namespace Sbd.DoStuff.WebApp.Editing;

/// <summary>Mutable working copy of one <see cref="Sbd.DoStuff.Domain.Lists.TaskListEntry"/> while it is being edited.</summary>
public sealed class EntryEditModel(string taskId, IEnumerable<string> categories, IEnumerable<KeyValuePair<string, string>>? values)
{
    public Guid Key { get; } = Guid.NewGuid();
    public string TaskId { get; } = taskId;
    public List<string> Categories { get; } = categories.ToList();

    /// <summary>Only the parameter values that differ from what the definition already provides.</summary>
    public Dictionary<string, string> Values { get; } = values?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];

    public bool Expanded { get; set; }
    public TaskRun? TestRun { get; set; }
    public string? TestError { get; set; }
}
