namespace Sbd.DoStuff.Domain.Lists;

public sealed class CategoryNode(string segment, string fullPath)
{
    private readonly Dictionary<string, CategoryNode> _children = new();
    private readonly List<TaskListEntryView> _tasks = new();

    public string Segment { get; } = segment;
    public string FullPath { get; } = fullPath;
    public IReadOnlyDictionary<string, CategoryNode> Children => _children;
    public IReadOnlyList<TaskListEntryView> Tasks => _tasks;

    internal CategoryNode GetOrAddChild(string childSegment, string childFullPath)
    {
        if (!_children.TryGetValue(childSegment, out var child))
        {
            child = new CategoryNode(childSegment, childFullPath);
            _children[childSegment] = child;
        }

        return child;
    }

    internal void AddTask(TaskListEntryView view) => _tasks.Add(view);
}
