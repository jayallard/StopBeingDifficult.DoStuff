using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.UnitTests.Fakes;

internal sealed class FakeTaskLibrary(params TaskDefinition[] definitions) : ITaskLibrary
{
    private readonly Dictionary<string, TaskDefinition> _definitions = definitions.ToDictionary(d => d.Id);

    public IReadOnlyList<TaskDefinition> GetAll() => _definitions.Values.ToList();

    public IReadOnlyList<TaskLibraryGroup> GetGroups() =>
        [new TaskLibraryGroup("fake", "Fake", _definitions.Values.ToList())];

    public TaskDefinition? Find(string taskId) => _definitions.GetValueOrDefault(taskId);

    public IReadOnlyList<string> StorageDirectories => ["library"];

    public TaskLibraryGroup CreateGroup(string name, string storageDirectory) =>
        new(name, name, []);
}
