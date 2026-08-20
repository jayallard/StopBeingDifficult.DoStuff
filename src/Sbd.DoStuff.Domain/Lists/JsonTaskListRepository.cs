using System.Text.Json;

namespace Sbd.DoStuff.Domain.Lists;

internal sealed class JsonTaskListRepository : ITaskListRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, TaskListDefinition> _lists = new();

    public JsonTaskListRepository(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var json = File.ReadAllText(file);
            var list = JsonSerializer.Deserialize<TaskListDefinition>(json, SerializerOptions)
                ?? throw new InvalidOperationException($"Task list file '{file}' did not deserialize.");

            if (!_lists.TryAdd(list.Id, list))
            {
                throw new InvalidOperationException($"Duplicate task list id '{list.Id}' (found in '{file}').");
            }
        }
    }

    public IReadOnlyList<TaskListDefinition> GetAll() => _lists.Values.ToList();

    public TaskListDefinition? Find(string listId) => _lists.GetValueOrDefault(listId);
}
