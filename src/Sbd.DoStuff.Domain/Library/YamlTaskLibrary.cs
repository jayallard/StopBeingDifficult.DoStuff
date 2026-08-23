using Microsoft.Extensions.Logging;
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

    public YamlTaskLibrary(IEnumerable<string> directories, IEnumerable<string> artifactsDirectories, ILogger<YamlTaskLibrary> logger)
    {
        var artifactsDirectoryList = artifactsDirectories.ToList();

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                logger.LogWarning("Task library directory '{Directory}' does not exist; skipping.", directory);
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.yaml", new EnumerationOptions{RecurseSubdirectories = true}))
            {
                var yaml = File.ReadAllText(file);
                var definitions = Deserializer.Deserialize<TaskDefinition[]>(yaml)
                    ?? throw new InvalidOperationException($"Task library file '{file}' did not deserialize to an array.");

                foreach (var definition in definitions)
                {
                    Validate(definition, file);

                    var resolved = definition.ScriptPath is null
                        ? definition
                        : definition with { ScriptPath = ResolveScriptPath(definition, artifactsDirectoryList, file) };

                    if (!_definitions.TryAdd(resolved.Id, resolved))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate task definition id '{resolved.Id}' (found in '{file}').");
                    }
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
            if (definition.Command is not null && definition.ScriptPath is not null)
            {
                throw new InvalidOperationException(
                    $"Task definition '{definition.Id}' (in '{file}') sets both Command and ScriptPath — " +
                    "a task's script must come from exactly one of them.");
            }

            return;
        }

        if (definition.Type is not null || definition.Command is not null || definition.ScriptPath is not null
            || definition.WorkingDirectory is not null || definition.EnvironmentVariables is not null
            || definition.Parameters is not null || definition.UseWindowsPowerShell is not null)
        {
            throw new InvalidOperationException(
                $"Task definition '{definition.Id}' (in '{file}') sets BaseTaskId and also sets " +
                "Type/Command/ScriptPath/WorkingDirectory/EnvironmentVariables/Parameters/UseWindowsPowerShell — " +
                "a derived definition must inherit all of these from its base, not specify them directly.");
        }
    }

    private static string ResolveScriptPath(TaskDefinition definition, IReadOnlyList<string> artifactsDirectories, string file)
    {
        if (Path.IsPathRooted(definition.ScriptPath))
        {
            var resolved = definition.ScriptPath!;
            if (!File.Exists(resolved))
            {
                throw new InvalidOperationException(
                    $"Task definition '{definition.Id}' (in '{file}') has ScriptPath '{definition.ScriptPath}', " +
                    $"which does not resolve to an existing file ('{resolved}').");
            }

            return resolved;
        }

        foreach (var artifactsDirectory in artifactsDirectories)
        {
            var candidate = Path.GetFullPath(Path.Combine(artifactsDirectory, definition.ScriptPath!));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Task definition '{definition.Id}' (in '{file}') has ScriptPath '{definition.ScriptPath}', " +
            $"which does not resolve to an existing file in any artifacts directory " +
            $"({string.Join(", ", artifactsDirectories)}).");
    }
}
