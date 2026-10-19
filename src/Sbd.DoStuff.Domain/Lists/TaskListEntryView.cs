using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.Domain.Lists;

/// <param name="Icon">The icon name from the entry, else the definition; null means the library default.</param>
/// <param name="Source">The list entry this view was resolved from, so a UI can edit and save it back.</param>
public sealed record TaskListEntryView(
    EffectiveTaskDefinition Definition,
    IReadOnlyDictionary<string, string> ParameterValues,
    string? Notes = null,
    string? Name = null,
    TaskListEntry? Source = null,
    string? DisabledReason = null,
    string? Icon = null)
{
    public bool IsDisabled => DisabledReason is not null;
}
