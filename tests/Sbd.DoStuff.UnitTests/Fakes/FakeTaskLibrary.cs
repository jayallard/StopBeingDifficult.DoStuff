using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.UnitTests.Fakes;

internal sealed class FakeTaskLibrary(params TaskDefinition[] definitions) : ITaskLibrary
{
    private readonly Dictionary<string, TaskDefinition> _definitions = definitions.ToDictionary(d => d.Id);

    public IReadOnlyList<TaskDefinition> GetAll() => _definitions.Values.ToList();

    public TaskDefinition? Find(string taskId) => _definitions.GetValueOrDefault(taskId);
}
