namespace Sbd.DoStuff.Domain.Lists;

public interface ITaskListRepository
{
    IReadOnlyList<TaskListDefinition> GetAll();
    TaskListDefinition? Find(string listId);

    /// <summary>Where a list is stored (its file path), or null if it doesn't exist.</summary>
    string? GetLocation(string listId);

    /// <summary>
    /// The task list folders lists can be saved into: one per configured <c>FileDataSource:Directories</c>
    /// entry, in configuration order. The folders need not exist yet.
    /// </summary>
    IReadOnlyList<string> StorageDirectories { get; }

    /// <summary>The storage directory an existing list lives under, or null if it doesn't exist.</summary>
    string? GetStorageDirectory(string listId);

    /// <summary>
    /// Creates or updates a list in <paramref name="storageDirectory"/> (one of <see cref="StorageDirectories"/>).
    /// Pass <paramref name="originalId"/> when editing an existing list (so a changed id is treated as a rename
    /// rather than a duplicate). Changing the storage directory moves the file.
    /// </summary>
    void Save(TaskListDefinition list, string storageDirectory, string? originalId = null);

    void Delete(string listId);
}
