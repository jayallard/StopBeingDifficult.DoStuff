namespace Sbd.DoStuff.Domain.Library;

public interface ITaskLibrary
{
    IReadOnlyList<TaskDefinition> GetAll();
    TaskDefinition? Find(string taskId);
}
