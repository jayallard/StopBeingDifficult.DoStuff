using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.Domain.Icons;

/// <param name="Description">Human-readable user, e.g. "Definition: Git Pull" or "List: Daily, entry: Git Pull".</param>
/// <param name="Link">An app-relative link to where it's used, or null.</param>
public sealed record IconUsage(string Description, string? Link);

/// <summary>Where an icon is referred to, keyed by its normalised (extension-less) name.</summary>
public sealed record IconReference(string Name, IReadOnlyList<IconUsage> Usages);

/// <summary>Finds which task definitions and task list entries name which icons.</summary>
public sealed class IconUsageScanner(ITaskLibrary library, ITaskListRepository lists, TaskIconCatalog catalog)
{
    public IReadOnlyList<IconReference> Scan()
    {
        var usages = new Dictionary<string, List<IconUsage>>(StringComparer.OrdinalIgnoreCase);

        void Add(string? icon, IconUsage usage)
        {
            if (string.IsNullOrWhiteSpace(icon))
            {
                return;
            }

            var name = TaskIconCatalog.NormalizeName(icon);
            if (!usages.TryGetValue(name, out var list))
            {
                usages[name] = list = [];
            }

            list.Add(usage);
        }

        // Tasks without an icon of their own show the inherited one, else the library default.
        var effectiveIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in library.GetAll())
        {
            string? inherited;
            try
            {
                inherited = TaskDefinitionResolver.Resolve(definition, library).Icon;
            }
            catch (Exception)
            {
                inherited = definition.Icon; // broken chain; startup validation reports it
            }

            var effective = string.IsNullOrWhiteSpace(inherited) ? catalog.DefaultIcon : inherited;
            effectiveIcons[definition.Id] = effective;
            Add(effective, new IconUsage($"Definition: {definition.Name}", null));
        }

        foreach (var list in lists.GetAll())
        {
            foreach (var entry in list.Entries)
            {
                var entryName = entry.Name ?? library.Find(entry.TaskId)?.Name ?? entry.TaskId;
                var icon = entry.Icon is { } own && !string.IsNullOrWhiteSpace(own)
                    ? own
                    : effectiveIcons.GetValueOrDefault(entry.TaskId, catalog.DefaultIcon);
                Add(icon, new IconUsage($"List: {list.Name}, entry: {entryName}", $"lists/{Uri.EscapeDataString(list.Id)}"));
            }
        }

        Add(catalog.DefaultIcon, new IconUsage("Library default icon", null));

        return usages
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new IconReference(pair.Key, pair.Value))
            .ToList();
    }
}
