using Sbd.DoStuff.Domain.Serialization;
using YamlDotNet.Serialization;

namespace Sbd.DoStuff.Domain.Library;

/// <summary>
/// Holds raw definitions (data, including unresolved derived ones), not executable ITask
/// instances — a definition alone may not have its parameters or base chain resolved yet.
/// </summary>
internal sealed class YamlTaskLibrary : ITaskLibrary
{
    private static readonly IDeserializer Deserializer = YamlDeserializerFactory.Create();

    private readonly Dictionary<string, TaskDefinition> _definitions = new();

    public YamlTaskLibrary(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.yaml"))
        {
            var yaml = File.ReadAllText(file);
            var definitions = Deserializer.Deserialize<TaskDefinition[]>(yaml)
                ?? throw new InvalidOperationException($"Task library file '{file}' did not deserialize to an array.");

            foreach (var definition in definitions)
            {
                Validate(definition, file);

                if (!_definitions.TryAdd(definition.Id, definition))
                {
                    throw new InvalidOperationException(
                        $"Duplicate task definition id '{definition.Id}' (found in '{file}').");
                }
            }
        }
    }

    public IReadOnlyList<TaskDefinition> GetAll() => _definitions.Values.ToList();

    public TaskDefinition? Find(string taskId) => _definitions.GetValueOrDefault(taskId);

    private static void Validate(TaskDefinition definition, string file)
    {
        if (definition.BaseTaskId is null)
        {
            return;
        }

        if (definition.Type is not null || definition.Command is not null || definition.WorkingDirectory is not null
            || definition.EnvironmentVariables is not null || definition.Parameters is not null)
        {
            throw new InvalidOperationException(
                $"Task definition '{definition.Id}' (in '{file}') sets BaseTaskId and also sets " +
                "Type/Command/WorkingDirectory/EnvironmentVariables/Parameters — a derived definition must " +
                "inherit all of these from its base, not specify them directly.");
        }
    }
}
