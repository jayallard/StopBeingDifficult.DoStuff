using Microsoft.Extensions.Logging;
using Sbd.DoStuff.Domain.Serialization;
using YamlDotNet.Serialization;

namespace Sbd.DoStuff.Domain.Lists;

internal sealed class YamlTaskListRepository : ITaskListRepository
{
    private static readonly IDeserializer Deserializer = YamlDeserializerFactory.Create();
    private static readonly ISerializer Serializer = YamlSerializerFactory.Create();

    private readonly record struct Stored(TaskListDefinition List, string FilePath, TaskListScope Scope);

    private readonly Dictionary<string, Stored> _lists = new();
    private readonly TaskListStorageOptions? _storage;
    private readonly object _gate = new();

    public YamlTaskListRepository(
        IEnumerable<string> directories,
        ILogger<YamlTaskListRepository> logger,
        TaskListStorageOptions? storage = null)
    {
        _storage = storage;

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

                if (!_lists.TryAdd(list.Id, new Stored(list, file, ScopeOf(file))))
                {
                    throw new InvalidOperationException($"Duplicate task list id '{list.Id}' (found in '{file}').");
                }
            }
        }
    }

    public IReadOnlyList<TaskListDefinition> GetAll()
    {
        lock (_gate)
        {
            return _lists.Values.Select(s => s.List).ToList();
        }
    }

    public TaskListDefinition? Find(string listId)
    {
        lock (_gate)
        {
            return _lists.TryGetValue(listId, out var stored) ? stored.List : null;
        }
    }

    public TaskListScope? GetScope(string listId)
    {
        lock (_gate)
        {
            return _lists.TryGetValue(listId, out var stored) ? stored.Scope : null;
        }
    }

    public void Save(TaskListDefinition list, TaskListScope scope, string? originalId = null)
    {
        var storage = _storage ?? throw new InvalidOperationException("Task list storage directories are not configured.");
        if (!TaskListValidator.IsValidId(list.Id))
        {
            throw new ArgumentException($"'{list.Id}' is not a valid task list id.", nameof(list));
        }

        lock (_gate)
        {
            Stored original = default;
            var hasOriginal = originalId is not null && _lists.TryGetValue(originalId, out original);

            if ((!hasOriginal || list.Id != originalId) && _lists.ContainsKey(list.Id))
            {
                throw new InvalidOperationException($"A task list with id '{list.Id}' already exists.");
            }

            // An edit that keeps its id and scope rewrites its own file (whatever it is named);
            // anything else goes to <scope directory>/<id>.yaml.
            var target = hasOriginal && list.Id == originalId && original.Scope == scope
                ? original.FilePath
                : Path.Combine(storage.DirectoryFor(scope), list.Id + ".yaml");

            if (File.Exists(target) && !(hasOriginal && SamePath(target, original.FilePath)))
            {
                throw new InvalidOperationException($"File '{target}' already exists and belongs to another task list.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, Serializer.Serialize(list));

            if (hasOriginal)
            {
                if (!SamePath(target, original.FilePath))
                {
                    File.Delete(original.FilePath);
                }

                _lists.Remove(originalId!);
            }

            _lists[list.Id] = new Stored(list, target, scope);
        }
    }

    public void Delete(string listId)
    {
        lock (_gate)
        {
            if (_lists.Remove(listId, out var stored))
            {
                File.Delete(stored.FilePath);
            }
        }
    }

    private TaskListScope ScopeOf(string file) =>
        _storage is not null && IsUnder(file, _storage.PrivateDirectory) ? TaskListScope.Private : TaskListScope.Public;

    private static bool IsUnder(string file, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
