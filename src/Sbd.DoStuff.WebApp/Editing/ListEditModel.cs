using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Editing;

/// <summary>Mutable working copy of a <see cref="TaskListDefinition"/> for the editor page.</summary>
public sealed class ListEditModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public TaskListScope Scope { get; set; } = TaskListScope.Private;
    public List<EntryEditModel> Entries { get; } = [];

    /// <summary>Categories created in the editor that don't hold any entry yet (so there is somewhere to drop into). Not persisted.</summary>
    public SortedSet<string> EmptyCategories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ListEditModel From(TaskListDefinition list, TaskListScope scope)
    {
        var model = new ListEditModel { Id = list.Id, Name = list.Name, Description = list.Description, Scope = scope };
        foreach (var entry in list.Entries)
        {
            model.Entries.Add(new EntryEditModel(entry.TaskId, entry.Categories, entry.ParameterValues, entry.Notes));
        }

        return model;
    }

    public TaskListDefinition ToDefinition() => new(
        Id.Trim(),
        Name.Trim(),
        string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        Entries.Select(e => new TaskListEntry(
            e.TaskId,
            e.Categories.ToList(),
            e.Values.Count == 0 ? null : new Dictionary<string, string>(e.Values),
            string.IsNullOrWhiteSpace(e.Notes) ? null : e.Notes.Trim())).ToList());

    public IEnumerable<string> AllCategories =>
        Entries.SelectMany(e => e.Categories).Concat(EmptyCategories).Distinct(StringComparer.OrdinalIgnoreCase).Order();

    /// <summary>Every category in the tree as a dot path — parents of deeper paths and empty categories included.</summary>
    public IEnumerable<string> AllCategoryPaths => AllCategories
        .SelectMany(category => category.Split('.')
            .Select((_, i) => string.Join('.', category.Split('.').Take(i + 1))))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase);

    public EntryEditModel AddEntry(string taskId, string category)
    {
        var entry = new EntryEditModel(taskId, [category], null) { Expanded = true };
        Entries.Add(entry);
        EmptyCategories.Remove(category);
        return entry;
    }

    public void RemoveEntry(EntryEditModel entry) => Entries.Remove(entry);

    public void AddCategory(string category)
    {
        if (!AllCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            EmptyCategories.Add(category);
        }
    }

    public void AddEntryToCategory(EntryEditModel entry, string category)
    {
        if (!entry.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            entry.Categories.Add(category);
        }

        EmptyCategories.Remove(category);
    }

    /// <summary>Removes the entry from one category; an entry always keeps at least one.</summary>
    public bool RemoveEntryFromCategory(EntryEditModel entry, string category)
    {
        if (entry.Categories.Count <= 1)
        {
            return false;
        }

        entry.Categories.RemoveAll(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    /// <summary>Renames a category and everything beneath it ("a.b" becomes "x.b" when "a" is renamed to "x").</summary>
    public void RenameCategory(string oldPath, string newPath)
    {
        string Rewrite(string category) =>
            category.Equals(oldPath, StringComparison.OrdinalIgnoreCase) ? newPath
            : category.StartsWith(oldPath + ".", StringComparison.OrdinalIgnoreCase) ? newPath + category[oldPath.Length..]
            : category;

        foreach (var entry in Entries)
        {
            var renamed = entry.Categories.Select(Rewrite).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            entry.Categories.Clear();
            entry.Categories.AddRange(renamed);
        }

        var renamedEmpty = EmptyCategories.Select(Rewrite).ToList();
        EmptyCategories.Clear();
        foreach (var category in renamedEmpty)
        {
            EmptyCategories.Add(category);
        }
    }

    /// <summary>The category currently being dragged in the editor, if any. UI state only.</summary>
    public string? DraggedCategory { get; set; }

    /// <summary>
    /// Moves a category (and everything beneath it) under <paramref name="newParent"/>; an empty parent makes it top-level.
    /// Returns false when the move is a no-op or would put a category inside itself.
    /// </summary>
    public bool MoveCategory(string path, string newParent)
    {
        var segment = path[(path.LastIndexOf('.') + 1)..];
        var newPath = newParent.Length == 0 ? segment : $"{newParent}.{segment}";

        if (newPath.Equals(path, StringComparison.OrdinalIgnoreCase)
            || newParent.Equals(path, StringComparison.OrdinalIgnoreCase)
            || newParent.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        RenameCategory(path, newPath);
        return true;
    }

    /// <summary>
    /// Handles a drag of <paramref name="entry"/> from category <paramref name="from"/> to position
    /// <paramref name="newIndex"/> among the entries shown under <paramref name="to"/>. Moving between
    /// categories changes membership; the position is expressed by reordering <see cref="Entries"/>.
    /// </summary>
    public void Move(EntryEditModel entry, string from, string to, int newIndex)
    {
        if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            if (!entry.Categories.Contains(to, StringComparer.OrdinalIgnoreCase))
            {
                entry.Categories.Add(to);
            }

            if (entry.Categories.Count > 1)
            {
                entry.Categories.RemoveAll(c => string.Equals(c, from, StringComparison.OrdinalIgnoreCase));
            }

            EmptyCategories.Remove(to);

            // Dragging out a category's last task leaves the category in place as a drop target.
            if (!Entries.Any(e => e.Categories.Contains(from, StringComparer.OrdinalIgnoreCase)))
            {
                EmptyCategories.Add(from);
            }
        }

        var siblings = Entries.Where(e => e != entry && e.Categories.Contains(to, StringComparer.OrdinalIgnoreCase)).ToList();
        Entries.Remove(entry);

        int insertAt;
        if (newIndex < siblings.Count)
        {
            insertAt = Entries.IndexOf(siblings[Math.Max(newIndex, 0)]);
        }
        else if (siblings.Count > 0)
        {
            insertAt = Entries.IndexOf(siblings[^1]) + 1;
        }
        else
        {
            insertAt = Entries.Count;
        }

        Entries.Insert(insertAt, entry);
    }

    public EditCategoryNode BuildTree()
    {
        var root = new EditCategoryNode("", "");

        EditCategoryNode Ensure(string category)
        {
            var node = root;
            var path = "";
            foreach (var segment in category.Split('.'))
            {
                path = path.Length == 0 ? segment : $"{path}.{segment}";
                if (!node.Children.TryGetValue(segment, out var child))
                {
                    child = new EditCategoryNode(segment, path);
                    node.Children[segment] = child;
                }

                node = child;
            }

            return node;
        }

        foreach (var entry in Entries)
        {
            foreach (var category in entry.Categories)
            {
                Ensure(category).Entries.Add(entry);
            }
        }

        foreach (var category in EmptyCategories)
        {
            Ensure(category);
        }

        return root;
    }
}
