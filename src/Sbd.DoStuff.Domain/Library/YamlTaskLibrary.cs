using Microsoft.Extensions.Logging;
using Sbd.DoStuff.Domain.Lists;
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
    private readonly List<TaskLibraryGroup> _groups = [];

    private readonly string[] _storageDirectories;
    private readonly object _gate = new();

    public YamlTaskLibrary(
        IEnumerable<string> directories,
        IEnumerable<string> assetsDirectories,
        ILogger<YamlTaskLibrary> logger,
        IEnumerable<string>? storageDirectories = null)
    {
        _storageDirectories = (storageDirectories ?? []).Select(Path.GetFullPath).ToArray();
        var assetsDirectoryList = assetsDirectories.ToList();

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

                var fileDefinitions = new List<TaskDefinition>();

                foreach (var definition in definitions)
                {
                    Validate(definition, file);

                    var resolved = definition.ScriptPath is null
                        ? definition
                        : definition with { ScriptPath = ResolveScriptPath(definition, assetsDirectoryList, file) };

                    if (!_definitions.TryAdd(resolved.Id, resolved))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate task definition id '{resolved.Id}' (found in '{file}').");
                    }

                    fileDefinitions.Add(resolved);
                }

                var baseId = Path.GetFileNameWithoutExtension(file);
                var id = baseId;
                for (var suffix = 2; _groups.Any(g => g.Id == id); suffix++)
                {
                    id = $"{baseId}-{suffix}";
                }

                _groups.Add(new TaskLibraryGroup(id, FormatName(baseId), fileDefinitions, file));
            }
        }

        _groups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<TaskDefinition> GetAll() => _definitions.Values.ToList();

    public IReadOnlyList<TaskLibraryGroup> GetGroups()
    {
        lock (_gate)
        {
            return _groups.ToList();
        }
    }

    public IReadOnlyList<string> StorageDirectories => _storageDirectories;

    public TaskLibraryGroup CreateGroup(string name, string storageDirectory)
    {
        var storage = _storageDirectories.FirstOrDefault(d => string.Equals(d, Path.GetFullPath(storageDirectory), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"'{storageDirectory}' is not a configured task library folder.");
        if (!TaskListValidator.IsValidId(name))
        {
            throw new ArgumentException($"'{name}' is not a valid task library name.", nameof(name));
        }

        lock (_gate)
        {
            var file = Path.Combine(storage, name + ".yaml");
            if (File.Exists(file) || _groups.Any(g => string.Equals(g.Id, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A task library named '{name}' already exists.");
            }

            Directory.CreateDirectory(storage);
            File.WriteAllText(file, "[]" + Environment.NewLine);

            var group = new TaskLibraryGroup(name, FormatName(name), [], file);
            _groups.Add(group);
            _groups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return group;
        }
    }

    // "git-library" -> "Git"; "vscode-libary" (sic) -> "Vscode".
    private static string FormatName(string fileName)
    {
        var name = fileName;
        foreach (var suffix in new[] { "-library", "-libary", ".library" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        name = name.Replace('-', ' ').Replace('_', ' ');
        return name.Length == 0 ? fileName : char.ToUpperInvariant(name[0]) + name[1..];
    }

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
            || definition.Parameters is not null)
        {
            throw new InvalidOperationException(
                $"Task definition '{definition.Id}' (in '{file}') sets BaseTaskId and also sets " +
                "Type/Command/ScriptPath/WorkingDirectory/EnvironmentVariables/Parameters — " +
                "a derived definition must inherit all of these from its base, not specify them directly.");
        }
    }

    private static string ResolveScriptPath(TaskDefinition definition, IReadOnlyList<string> assetsDirectories, string file)
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

        foreach (var assetsDirectory in assetsDirectories)
        {
            var candidate = Path.GetFullPath(Path.Combine(assetsDirectory, definition.ScriptPath!));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Task definition '{definition.Id}' (in '{file}') has ScriptPath '{definition.ScriptPath}', " +
            $"which does not resolve to an existing file in any assets directory " +
            $"({string.Join(", ", assetsDirectories)}).");
    }
}
