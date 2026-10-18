using Sbd.DoStuff.Domain.Execution;

namespace Sbd.DoStuff.WebApp.Editing;

/// <summary>Mutable working copy of one <see cref="Sbd.DoStuff.Domain.Lists.TaskListEntry"/> while it is being edited.</summary>
public sealed class EntryEditModel(string taskId, IEnumerable<string> categories, IEnumerable<KeyValuePair<string, string>>? values, string? notes = null, string? name = null, string? disabledReason = null)
{
    public Guid Key { get; } = Guid.NewGuid();
    public string TaskId { get; } = taskId;
    public List<string> Categories { get; } = categories.ToList();

    /// <summary>Only the parameter values that differ from what the definition already provides.</summary>
    public Dictionary<string, string> Values { get; } = values?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [];

    /// <summary>Optional display name overriding the task definition's name in this list.</summary>
    public string? Name { get; set; } = name;

    /// <summary>Optional context about why this task is in the list.</summary>
    public string? Notes { get; set; } = notes;

    /// <summary>Whether the task is disabled in this list (shown, but not runnable).</summary>
    public bool Disabled { get; set; } = disabledReason is not null;

    /// <summary>Why the task is disabled; only meaningful while <see cref="Disabled"/> is set.</summary>
    public string? DisabledReason { get; set; } = disabledReason;

    /// <summary>The value to persist: null when enabled; otherwise the trimmed reason (never empty, so the entry stays disabled).</summary>
    public string? PersistedDisabledReason =>
        !Disabled ? null : string.IsNullOrWhiteSpace(DisabledReason) ? "Disabled" : DisabledReason.Trim();

    public bool Expanded { get; set; }
    public TaskRun? TestRun { get; set; }
    public string? TestError { get; set; }
}
