using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Editing;

/// <summary>One category in the organizer tree. The root node has an empty name and is never shown.</summary>
public sealed class OrganizerNode(string name, OrganizerNode? parent)
{
    /// <summary>Stable identity for the DOM and drag-and-drop; paths change when categories move.</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string Name { get; } = name;
    public OrganizerNode? Parent { get; set; } = parent;
    public List<OrganizerNode> Children { get; } = [];

    /// <summary>Tasks filed directly under this category, in display order.</summary>
    public List<EntryEditModel> Entries { get; } = [];

    public bool IsRoot => Parent is null;

    /// <summary>Dot-notation path as stored in the task list, e.g. "cleanup.temp".</summary>
    public string Path => Parent is null ? "" : Parent.IsRoot ? Name : $"{Parent.Path}.{Name}";

    public bool IsWithin(OrganizerNode ancestor)
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Working copy of a task list arranged as an explicit category tree, for the organizer page.
/// The tree is the source of truth; <see cref="ToEntries"/> flattens it back into list entries.
/// </summary>
public sealed class OrganizerModel
{
    public OrganizerNode Root { get; } = new("", null);

    public static OrganizerModel From(TaskListDefinition list)
    {
        var model = new OrganizerModel();
        foreach (var source in list.Entries)
        {
            var entry = new EntryEditModel(source.TaskId, source.Categories, source.ParameterValues, source.Notes, source.Name, source.DisabledReason) { Icon = source.Icon };
            if (source.Categories.Count == 0)
            {
                // Kept at the root so an uncategorized entry is not silently lost on save.
                model.Root.Entries.Add(entry);
            }

            foreach (var category in source.Categories)
            {
                model.EnsurePath(category).Entries.Add(entry);
            }
        }

        return model;
    }

    /// <summary>Every category in tree order (parents before children), for "move to" pickers.</summary>
    public IEnumerable<OrganizerNode> AllCategories()
    {
        IEnumerable<OrganizerNode> Walk(OrganizerNode node) =>
            node.Children.SelectMany(child => child.IsRoot ? [] : new[] { child }.Concat(Walk(child)));

        return Walk(Root);
    }

    public OrganizerNode? Find(string id) => Find(Root, id);

    private static OrganizerNode? Find(OrganizerNode node, string id)
    {
        if (node.Id == id)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (Find(child, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private OrganizerNode EnsurePath(string path)
    {
        var node = Root;
        foreach (var segment in path.Split('.').Select(s => s.Trim()))
        {
            var child = node.Children.FirstOrDefault(c => c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (child is null)
            {
                child = new OrganizerNode(segment, node);
                node.Children.Add(child);
            }

            node = child;
        }

        return node;
    }

    /// <summary>Returns an error message, or null when the category was added.</summary>
    public string? AddCategory(OrganizerNode parent, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return "Enter a name.";
        }

        if (name.Contains('.'))
        {
            return "A name can't contain '.' — add subcategories under their parent instead.";
        }

        if (HasChildNamed(parent, name))
        {
            return $"'{name}' already exists here.";
        }

        parent.Children.Add(new OrganizerNode(name, parent));
        return null;
    }

    public EntryEditModel AddEntry(string taskId, OrganizerNode category)
    {
        var entry = new EntryEditModel(taskId, [category.Path], null);
        category.Entries.Add(entry);
        return entry;
    }

    /// <summary>Copies the task into the same category, directly after the original.</summary>
    public EntryEditModel CloneEntry(EntryEditModel entry, OrganizerNode category)
    {
        var clone = new EntryEditModel(entry.TaskId, category.IsRoot ? [] : [category.Path], entry.Values, entry.Notes, entry.Name)
        {
            Disabled = entry.Disabled,
            DisabledReason = entry.DisabledReason,
            Icon = entry.Icon,
        };
        var index = category.Entries.IndexOf(entry);
        category.Entries.Insert(index < 0 ? category.Entries.Count : index + 1, clone);
        return clone;
    }

    /// <summary>Removes the task from one category; the task leaves the list once no category holds it.</summary>
    public void RemoveEntry(EntryEditModel entry, OrganizerNode category) => category.Entries.Remove(entry);

    /// <summary>
    /// Moves a category under <paramref name="newParent"/> (the root makes it top-level) at
    /// <paramref name="index"/> among that parent's children. Returns an error message, or null on success.
    /// </summary>
    public string? MoveCategory(OrganizerNode node, OrganizerNode newParent, int index)
    {
        if (node.IsRoot || newParent.IsWithin(node))
        {
            return "A category can't be moved into itself.";
        }

        if (!ReferenceEquals(node.Parent, newParent) && HasChildNamed(newParent, node.Name))
        {
            var where = newParent.IsRoot ? "The top level" : $"'{newParent.Path}'";
            return $"{where} already has a category named '{node.Name}'.";
        }

        node.Parent!.Children.Remove(node);
        node.Parent = newParent;
        newParent.Children.Insert(Math.Clamp(index, 0, newParent.Children.Count), node);
        return null;
    }

    /// <summary>
    /// Moves a task to position <paramref name="index"/> within <paramref name="to"/>. When it is dropped into a
    /// category that already holds it, it is simply taken out of <paramref name="from"/>.
    /// </summary>
    public void MoveEntry(EntryEditModel entry, OrganizerNode from, OrganizerNode to, int index)
    {
        from.Entries.Remove(entry);
        if (!ReferenceEquals(from, to) && to.Entries.Contains(entry))
        {
            return;
        }

        to.Entries.Insert(Math.Clamp(index, 0, to.Entries.Count), entry);
    }

    /// <summary>Shifts a task one place up (-1) or down (+1) within its category.</summary>
    public void MoveEntryBy(EntryEditModel entry, OrganizerNode category, int delta)
    {
        var index = category.Entries.IndexOf(entry);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= category.Entries.Count)
        {
            return;
        }

        category.Entries.RemoveAt(index);
        category.Entries.Insert(target, entry);
    }

    /// <summary>
    /// Flattens the tree into list entries. Entries are emitted in tree order, so the saved file reproduces the
    /// category and task order on load. Categories holding no tasks anywhere beneath them are not representable.
    /// </summary>
    public List<TaskListEntry> ToEntries()
    {
        var order = new List<EntryEditModel>();
        var categories = new Dictionary<EntryEditModel, List<string>>();

        void Visit(OrganizerNode node)
        {
            foreach (var entry in node.Entries)
            {
                if (!categories.TryGetValue(entry, out var paths))
                {
                    categories[entry] = paths = [];
                    order.Add(entry);
                }

                if (!node.IsRoot)
                {
                    paths.Add(node.Path);
                }
            }

            foreach (var child in node.Children)
            {
                Visit(child);
            }
        }

        Visit(Root);

        return order.Select(e => new TaskListEntry(
            e.TaskId,
            categories[e],
            e.Values.Count == 0 ? null : new Dictionary<string, string>(e.Values),
            string.IsNullOrWhiteSpace(e.Notes) ? null : e.Notes.Trim(),
            string.IsNullOrWhiteSpace(e.Name) ? null : e.Name.Trim(),
            e.PersistedDisabledReason,
            string.IsNullOrWhiteSpace(e.Icon) ? null : e.Icon.Trim())).ToList();
    }

    private static bool HasChildNamed(OrganizerNode parent, string name) =>
        parent.Children.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
