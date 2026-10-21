using Microsoft.Extensions.Logging;
using Sbd.DoStuff.Domain.Serialization;
using YamlDotNet.Serialization;

namespace Sbd.DoStuff.Domain.Lists;

internal sealed class YamlTaskListRepository : ITaskListRepository
{
    private static readonly IDeserializer Deserializer = YamlDeserializerFactory.Create();
    private static readonly ISerializer Serializer = YamlSerializerFactory.Create();

    private readonly record struct Stored(TaskListDefinition List, string FilePath, string? StorageDirectory);

    private readonly Dictionary<string, Stored> _lists = new();
    private readonly string[] _storageDirectories;
    private readonly object _gate = new();

    public YamlTaskListRepository(
        IEnumerable<string> directories,
        ILogger<YamlTaskListRepository> logger,
        IEnumerable<string>? storageDirectories = null)
    {
        _storageDirectories = (storageDirectories ?? []).Select(Path.GetFullPath).ToArray();

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

                if (!_lists.TryAdd(list.Id, new Stored(list, file, StorageDirectoryOf(file))))
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

    public IReadOnlyList<string> StorageDirectories => _storageDirectories;

    public string? GetStorageDirectory(string listId)
    {
        lock (_gate)
        {
            return _lists.TryGetValue(listId, out var stored) ? stored.StorageDirectory : null;
        }
    }

    public string? GetLocation(string listId)
    {
        lock (_gate)
        {
            return _lists.TryGetValue(listId, out var stored) ? stored.FilePath : null;
        }
    }

    public void Save(TaskListDefinition list, string storageDirectory, string? originalId = null)
    {
        var storage = _storageDirectories.FirstOrDefault(d => SamePath(d, storageDirectory))
            ?? throw new InvalidOperationException($"'{storageDirectory}' is not a configured task list folder.");
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

            // An edit that keeps its id and folder rewrites its own file (whatever it is named);
            // anything else goes to <storage directory>/<id>.yaml.
            var target = hasOriginal && list.Id == originalId && original.StorageDirectory is not null && SamePath(original.StorageDirectory, storage)
                ? original.FilePath
                : Path.Combine(storage, list.Id + ".yaml");

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

            _lists[list.Id] = new Stored(list, target, storage);
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

    private string? StorageDirectoryOf(string file) =>
        _storageDirectories.FirstOrDefault(directory => IsUnder(file, directory));

    private static bool IsUnder(string file, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
