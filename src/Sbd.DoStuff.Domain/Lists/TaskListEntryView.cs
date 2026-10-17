using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.Domain.Lists;

/// <param name="Source">The list entry this view was resolved from, so a UI can edit and save it back.</param>
public sealed record TaskListEntryView(
    EffectiveTaskDefinition Definition,
    IReadOnlyDictionary<string, string> ParameterValues,
    string? Notes = null,
    string? Name = null,
    TaskListEntry? Source = null);
