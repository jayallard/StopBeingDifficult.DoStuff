using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.UnitTests.Fakes;

internal sealed class FakeTaskListRepository(params TaskListDefinition[] lists) : ITaskListRepository
{
    private readonly Dictionary<string, TaskListDefinition> _lists = lists.ToDictionary(l => l.Id);

    public IReadOnlyList<TaskListDefinition> GetAll() => _lists.Values.ToList();

    public TaskListDefinition? Find(string listId) => _lists.GetValueOrDefault(listId);
}
