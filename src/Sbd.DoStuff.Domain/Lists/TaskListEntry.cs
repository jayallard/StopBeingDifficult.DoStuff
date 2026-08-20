namespace Sbd.DoStuff.Domain.Lists;

public sealed record TaskListEntry(
    string TaskId,
    IReadOnlyList<string> Categories,
    IReadOnlyDictionary<string, string>? ParameterValues);
