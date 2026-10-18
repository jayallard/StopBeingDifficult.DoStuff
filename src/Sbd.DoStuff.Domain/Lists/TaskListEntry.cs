namespace Sbd.DoStuff.Domain.Lists;

/// <param name="DisabledReason">When non-null the entry is disabled: still shown, but it can't be run. The text explains why.</param>
public sealed record TaskListEntry(
    string TaskId,
    IReadOnlyList<string> Categories,
    IReadOnlyDictionary<string, string>? ParameterValues,
    string? Notes = null,
    string? Name = null,
    string? DisabledReason = null);
