namespace Sbd.DoStuff.Domain.Lists;

public interface ITaskListRepository
{
    IReadOnlyList<TaskListDefinition> GetAll();
    TaskListDefinition? Find(string listId);

    /// <summary>The storage scope of an existing list, or null if it doesn't exist.</summary>
    TaskListScope? GetScope(string listId);

    /// <summary>
    /// Creates or updates a list. Pass <paramref name="originalId"/> when editing an existing list
    /// (so a changed id is treated as a rename rather than a duplicate). Changing the scope moves the file.
    /// </summary>
    void Save(TaskListDefinition list, TaskListScope scope, string? originalId = null);

    void Delete(string listId);
}
