namespace Sbd.DoStuff.WebApp.Editing;

public sealed class EditCategoryNode(string segment, string path)
{
    public string Segment { get; } = segment;
    public string Path { get; } = path;
    /// <summary>Child categories in order of first appearance in the list (no removals, so insertion order is preserved).</summary>
    public Dictionary<string, EditCategoryNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Entries filed directly under this category (not its descendants), in list order.</summary>
    public List<EntryEditModel> Entries { get; } = [];
}
