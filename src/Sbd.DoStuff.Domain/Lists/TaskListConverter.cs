using System.Text.RegularExpressions;
using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.Domain.Lists;

public enum EntryConversionStatus
{
    /// <summary>Can be turned into a definition in the target library.</summary>
    Convertible,

    /// <summary>The entry already points at a definition that lives in the target library.</summary>
    InTargetLibrary,

    /// <summary>The entry just references an existing definition without changing anything, so there is nothing to convert.</summary>
    PlainReference,

    /// <summary>
    /// The entry's task comes from a data folder that is neither the system folder (the first configured
    /// one, which everything may rely on) nor the target library's own folder.
    /// </summary>
    OutsideSharedFolders,

    /// <summary>The entry's task id doesn't resolve to a definition.</summary>
    UnknownTask,
}

/// <param name="Index">Position of the entry within the list.</param>
/// <param name="Proposed">The derived definition that conversion would create (only for <see cref="EntryConversionStatus.Convertible"/>).</param>
public sealed record EntryConversion(
    int Index,
    TaskListEntry Entry,
    TaskDefinition? Base,
    EntryConversionStatus Status,
    TaskDefinition? Proposed);

/// <summary>
/// Turns task list entries into derived task definitions in a library, so other lists can share them.
/// An entry is a reference to a definition plus its own parameter values, name and icon; converting it
/// creates a definition (BaseTaskId = the entry's task) carrying those, and repoints the entry at the new
/// definition, leaving it with only list-specific data (categories, notes, disabled reason).
/// Only tasks from the system data folder (the first configured one) or from the target library's own
/// data folder are eligible, so a library never depends on a folder that may not exist elsewhere.
/// </summary>
public sealed partial class TaskListConverter(ITaskLibrary library, ITaskListRepository lists)
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonIdCharacters();

    /// <summary>
    /// The libraries a list's tasks may be converted into: those in the system folder, and those in the
    /// same data folder as the list itself (empty when the list is itself in the system folder).
    /// </summary>
    public (IReadOnlyList<TaskLibraryGroup> System, IReadOnlyList<TaskLibraryGroup> SameFolder) EligibleLibraries(string listId)
    {
        var groups = library.GetGroups();
        var systemRoot = library.StorageDirectories.Count > 0 ? DataFolderOf(library.StorageDirectories[0]) : null;
        var listStorage = lists.GetStorageDirectory(listId);
        var listRoot = listStorage is null ? null : DataFolderOf(listStorage);

        var system = groups.Where(g => SameFolder(DataFolderOf(RootOf(g.Source)), systemRoot)).ToList();
        var same = SameFolder(listRoot, systemRoot)
            ? []
            : groups.Where(g => SameFolder(DataFolderOf(RootOf(g.Source)), listRoot)).ToList();
        return (system, same);
    }

    // "<data folder>/TaskLibrary" or "<data folder>/TaskLists" -> "<data folder>".
    private static string? DataFolderOf(string? storageDirectory) =>
        storageDirectory is null ? null : Path.GetDirectoryName(Normalize(storageDirectory));

    private static bool SameFolder(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<EntryConversion> Plan(TaskListDefinition list, string targetLibraryId)
    {
        var target = library.GetGroups().FirstOrDefault(g => g.Id == targetLibraryId)
            ?? throw new InvalidOperationException($"Unknown task library '{targetLibraryId}'.");
        var targetRoot = RootOf(target.Source);
        var systemRoot = library.StorageDirectories.Count > 0 ? Normalize(library.StorageDirectories[0]) : null;
        var rootById = library.GetGroups()
            .SelectMany(g => g.Tasks.Select(t => (t.Id, Root: RootOf(g.Source))))
            .ToDictionary(x => x.Id, x => x.Root);
        var targetIds = target.Tasks.Select(t => t.Id).ToHashSet();
        var usedIds = library.GetAll().Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var plan = new List<EntryConversion>();
        for (var index = 0; index < list.Entries.Count; index++)
        {
            var entry = list.Entries[index];
            var baseDefinition = library.Find(entry.TaskId);

            if (baseDefinition is null)
            {
                plan.Add(new EntryConversion(index, entry, null, EntryConversionStatus.UnknownTask, null));
            }
            else if (targetIds.Contains(entry.TaskId))
            {
                plan.Add(new EntryConversion(index, entry, baseDefinition, EntryConversionStatus.InTargetLibrary, null));
            }
            else if (!IsShared(rootById.GetValueOrDefault(entry.TaskId), systemRoot, targetRoot))
            {
                plan.Add(new EntryConversion(index, entry, baseDefinition, EntryConversionStatus.OutsideSharedFolders, null));
            }
            else if (!CarriesOverrides(entry))
            {
                plan.Add(new EntryConversion(index, entry, baseDefinition, EntryConversionStatus.PlainReference, null));
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(entry.Name) ? baseDefinition.Name : entry.Name!;
                var id = UniqueId(Slug(name, entry.TaskId), usedIds);
                usedIds.Add(id);
                var values = entry.ParameterValues is { Count: > 0 } ? entry.ParameterValues : null;
                var proposed = new TaskDefinition(
                    id, name, null, entry.TaskId, values,
                    Type: null, Command: null, ScriptPath: null, WorkingDirectory: null,
                    EnvironmentVariables: null, Parameters: null, Icon: entry.Icon);
                plan.Add(new EntryConversion(index, entry, baseDefinition, EntryConversionStatus.Convertible, proposed));
            }
        }

        return plan;
    }

    /// <summary>
    /// Converts the entries at <paramref name="entryIndexes"/> (each must be convertible) and saves both the
    /// library and the list. Returns the number converted.
    /// </summary>
    public int Convert(string listId, string targetLibraryId, IReadOnlyCollection<int> entryIndexes)
    {
        var list = lists.Find(listId) ?? throw new InvalidOperationException($"Unknown task list '{listId}'.");
        var storage = lists.GetStorageDirectory(listId)
            ?? throw new InvalidOperationException($"Task list '{listId}' is not stored in a configured task list folder.");

        var selected = Plan(list, targetLibraryId)
            .Where(c => entryIndexes.Contains(c.Index))
            .ToList();
        var notConvertible = selected.FirstOrDefault(c => c.Status != EntryConversionStatus.Convertible);
        if (notConvertible is not null)
        {
            throw new InvalidOperationException(
                $"Entry '{notConvertible.Entry.TaskId}' can't be converted ({notConvertible.Status}).");
        }

        if (selected.Count == 0)
        {
            return 0;
        }

        library.AddDefinitions(targetLibraryId, selected.Select(c => c.Proposed!).ToList());

        var entries = list.Entries.ToList();
        foreach (var conversion in selected)
        {
            entries[conversion.Index] = conversion.Entry with
            {
                TaskId = conversion.Proposed!.Id,
                ParameterValues = null,
                Name = null,
                Icon = null,
            };
        }

        lists.Save(list with { Entries = entries }, storage, listId);
        return selected.Count;
    }

    // Library storage directories are "<data dir>/TaskLibrary"; the data dir is what identifies a folder structure.
    private string? RootOf(string? file)
    {
        if (file is null)
        {
            return null;
        }

        var full = Path.GetFullPath(file);
        return library.StorageDirectories
            .Select(Normalize)
            .FirstOrDefault(d => full.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string directory) =>
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsShared(string? taskRoot, string? systemRoot, string? targetRoot) =>
        taskRoot is not null
        && (string.Equals(taskRoot, systemRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(taskRoot, targetRoot, StringComparison.OrdinalIgnoreCase));

    private static bool CarriesOverrides(TaskListEntry entry) =>
        entry.ParameterValues is { Count: > 0 }
        || !string.IsNullOrWhiteSpace(entry.Name)
        || !string.IsNullOrWhiteSpace(entry.Icon);

    private static string Slug(string name, string fallback)
    {
        var slug = NonIdCharacters().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return slug.Length > 0 ? slug : fallback;
    }

    private static string UniqueId(string baseId, HashSet<string> used)
    {
        var id = baseId;
        for (var suffix = 2; used.Contains(id); suffix++)
        {
            id = $"{baseId}-{suffix}";
        }

        return id;
    }
}
