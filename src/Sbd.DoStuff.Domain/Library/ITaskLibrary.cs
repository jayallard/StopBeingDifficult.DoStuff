namespace Sbd.DoStuff.Domain.Library;

public interface ITaskLibrary
{
    IReadOnlyList<TaskDefinition> GetAll();

    /// <summary>The definitions grouped by the library file they were loaded from.</summary>
    IReadOnlyList<TaskLibraryGroup> GetGroups();
    TaskDefinition? Find(string taskId);
}
