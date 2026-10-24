using System.Text.Json;

namespace Sbd.DoStuff.WebApp.Services;

/// <summary>The task lists the user has starred, persisted to <c>~/.sbd/favorites.json</c>.</summary>
public sealed class FavoriteLists
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly HashSet<string> _ids;

    public FavoriteLists()
    {
        _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sbd", "favorites.json");
        _ids = Load(_path);
    }

    public event Action? Changed;

    public bool IsFavorite(string listId)
    {
        lock (_gate)
        {
            return _ids.Contains(listId);
        }
    }

    public IReadOnlyCollection<string> Ids
    {
        get
        {
            lock (_gate)
            {
                return _ids.ToArray();
            }
        }
    }

    public void Toggle(string listId)
    {
        lock (_gate)
        {
            if (!_ids.Remove(listId))
            {
                _ids.Add(listId);
            }

            Save();
        }

        Changed?.Invoke();
    }

    private static HashSet<string> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path));
                return new HashSet<string>(ids ?? [], StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable favorites file just means no favorites yet.
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_ids.Order().ToArray()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Favorites stay in memory for this run if the file can't be written.
        }
    }
}
