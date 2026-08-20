namespace Sbd.DoStuff.Domain.Lists;

public interface ITaskListRepository
{
    IReadOnlyList<TaskListDefinition> GetAll();
    TaskListDefinition? Find(string listId);
}
