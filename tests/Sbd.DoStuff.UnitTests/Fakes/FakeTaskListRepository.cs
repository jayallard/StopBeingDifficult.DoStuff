using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.UnitTests.Fakes;

internal sealed class FakeTaskListRepository(params TaskListDefinition[] lists) : ITaskListRepository
{
    private readonly Dictionary<string, TaskListDefinition> _lists = lists.ToDictionary(l => l.Id);

    public IReadOnlyList<TaskListDefinition> GetAll() => _lists.Values.ToList();

    public TaskListDefinition? Find(string listId) => _lists.GetValueOrDefault(listId);

    public IReadOnlyList<string> StorageDirectories => ["lists"];

    public string? GetStorageDirectory(string listId) => _lists.ContainsKey(listId) ? "lists" : null;

    public string? GetLocation(string listId) => null;

    public void Save(TaskListDefinition list, string storageDirectory, string? originalId = null)
    {
        _lists.Remove(originalId ?? list.Id);
        _lists[list.Id] = list;
    }

    public void Delete(string listId) => _lists.Remove(listId);
}
