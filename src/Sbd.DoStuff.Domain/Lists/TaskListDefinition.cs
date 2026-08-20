namespace Sbd.DoStuff.Domain.Lists;

public sealed record TaskListDefinition(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<TaskListEntry> Entries);
