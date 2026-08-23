using Microsoft.Extensions.Logging;
using Sbd.DoStuff.Domain.Serialization;
using YamlDotNet.Serialization;

namespace Sbd.DoStuff.Domain.Lists;

internal sealed class YamlTaskListRepository : ITaskListRepository
{
    private static readonly IDeserializer Deserializer = YamlDeserializerFactory.Create();

    private readonly Dictionary<string, TaskListDefinition> _lists = new();

    public YamlTaskListRepository(IEnumerable<string> directories, ILogger<YamlTaskListRepository> logger)
    {
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                logger.LogWarning("Task list directory '{Directory}' does not exist; skipping.", directory);
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.yaml", new EnumerationOptions{RecurseSubdirectories = true}))
            {
                var yaml = File.ReadAllText(file);
                var list = Deserializer.Deserialize<TaskListDefinition>(yaml)
                    ?? throw new InvalidOperationException($"Task list file '{file}' did not deserialize.");

                if (!_lists.TryAdd(list.Id, list))
                {
                    throw new InvalidOperationException($"Duplicate task list id '{list.Id}' (found in '{file}').");
                }
            }
        }
    }

    public IReadOnlyList<TaskListDefinition> GetAll() => _lists.Values.ToList();

    public TaskListDefinition? Find(string listId) => _lists.GetValueOrDefault(listId);
}
