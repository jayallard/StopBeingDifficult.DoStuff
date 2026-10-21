namespace Sbd.DoStuff.Domain.Library;

public interface ITaskLibrary
{
    IReadOnlyList<TaskDefinition> GetAll();

    /// <summary>The definitions grouped by the library file they were loaded from.</summary>
    IReadOnlyList<TaskLibraryGroup> GetGroups();
    TaskDefinition? Find(string taskId);

    /// <summary>
    /// The task library folders new libraries can be created in: one per configured <c>FileDataSource:Directories</c>
    /// entry, in configuration order. The folders need not exist yet.
    /// </summary>
    IReadOnlyList<string> StorageDirectories { get; }

    /// <summary>
    /// Creates an empty library (no definitions) named <paramref name="name"/> in <paramref name="storageDirectory"/>
    /// (one of <see cref="StorageDirectories"/>) and returns its group. Throws if the name is invalid or already used.
    /// </summary>
    TaskLibraryGroup CreateGroup(string name, string storageDirectory);

    /// <summary>
    /// Appends <paramref name="definitions"/> to the library file behind group <paramref name="groupId"/> and
    /// makes them findable. Existing file content (comments, formatting) is left untouched. Throws if an id is
    /// already used anywhere in the library, or the group is unknown.
    /// </summary>
    void AddDefinitions(string groupId, IReadOnlyList<TaskDefinition> definitions);
}
