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
    private static readonly ISerializer Serializer = YamlSerializerFactory.Create();

    private readonly Dictionary<string, TaskDefinition> _definitions = new();
    private readonly List<TaskLibraryGroup> _groups = [];

    private readonly Dictionary<string, string?> _rootById = new();
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

                    _rootById[resolved.Id] = RootOf(file);
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

        foreach (var group in _groups)
        {
            foreach (var definition in group.Tasks)
            {
                ValidateBaseFolder(definition, RootOf(group.Source), group.Source!);
            }
        }
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

    public void AddDefinitions(string groupId, IReadOnlyList<TaskDefinition> definitions)
    {
        lock (_gate)
        {
            var index = _groups.FindIndex(g => g.Id == groupId);
            if (index < 0)
            {
                throw new InvalidOperationException($"Unknown task library '{groupId}'.");
            }

            var group = _groups[index];
            if (group.Source is null)
            {
                throw new InvalidOperationException($"Task library '{groupId}' has no file to write to.");
            }

            var newIds = new HashSet<string>();
            foreach (var definition in definitions)
            {
                Validate(definition, group.Source);
                ValidateBaseFolder(definition, RootOf(group.Source), group.Source);
                if (_definitions.ContainsKey(definition.Id) || !newIds.Add(definition.Id))
                {
                    throw new InvalidOperationException($"Duplicate task definition id '{definition.Id}'.");
                }
            }

            if (definitions.Count == 0)
            {
                return;
            }

            // Append text rather than re-serializing the file, so hand-written comments and formatting survive.
            var existing = File.ReadAllText(group.Source);
            var trimmed = existing.Trim();
            var addition = Serializer.Serialize(definitions.ToArray());
            string content;
            if (trimmed.Length == 0 || trimmed == "[]")
            {
                content = addition;
            }
            else if (trimmed.StartsWith('['))
            {
                throw new InvalidOperationException($"Task library file '{group.Source}' uses flow style and can't be appended to.");
            }
            else
            {
                content = existing.TrimEnd() + Environment.NewLine + addition;
            }

            File.WriteAllText(group.Source, content);

            var root = RootOf(group.Source);
            foreach (var definition in definitions)
            {
                _definitions.Add(definition.Id, definition);
                _rootById[definition.Id] = root;
            }

            _groups[index] = group with { Tasks = group.Tasks.Concat(definitions).ToList() };
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

    // A derived definition may only inherit from the system folder (the first configured one, which every
    // install has) or from its own folder, so a library never depends on a folder that may not exist elsewhere.
    // Not enforced when no storage directories are configured. A missing base is reported elsewhere.
    private void ValidateBaseFolder(TaskDefinition definition, string? root, string file)
    {
        if (definition.BaseTaskId is null || _storageDirectories.Length == 0
            || !_rootById.TryGetValue(definition.BaseTaskId, out var baseRoot))
        {
            return;
        }

        var system = _storageDirectories[0];
        if (!SamePath(baseRoot, system) && !SamePath(baseRoot, root))
        {
            throw new InvalidOperationException(
                $"Task definition '{definition.Id}' (in '{file}') inherits '{definition.BaseTaskId}' from '{baseRoot}', " +
                $"which is neither the system folder ('{system}') nor its own folder.");
        }
    }

    private string? RootOf(string? file)
    {
        if (file is null)
        {
            return null;
        }

        var full = Path.GetFullPath(file);
        return _storageDirectories.FirstOrDefault(d =>
            full.StartsWith(d.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

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
