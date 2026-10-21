using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.Domain.DataSources;

/// <summary>
/// A directory holding optional <c>TaskLibrary</c>, <c>TaskLists</c> and <c>Assets</c> subfolders.
/// The library and list repository load from all file sources together; this reports which of
/// their items came from this directory, plus the files in its Assets folder.
/// </summary>
internal sealed class FileDataSource(string directory, ITaskLibrary library, ITaskListRepository lists) : IDataSource
{
    public const string TaskLibraryFolder = "TaskLibrary";
    public const string TaskListsFolder = "TaskLists";
    public const string AssetsFolder = "Assets";

    public string Type => "File";

    public string Location => directory;

    public IReadOnlyList<DataItem> GetItems()
    {
        var items = new List<DataItem>();
        var libraryRoot = Path.Combine(directory, TaskLibraryFolder);
        var listsRoot = Path.Combine(directory, TaskListsFolder);
        var assetsRoot = Path.Combine(directory, AssetsFolder);

        foreach (var group in library.GetGroups().Where(g => g.Source is not null && IsUnder(g.Source, libraryRoot)))
        {
            items.Add(new DataItem(
                DataItemKind.TaskLibrary,
                Path.GetRelativePath(libraryRoot, group.Source!),
                group.Source!,
                group.Tasks.Select(t => t.Id).ToList(),
                SizeOf(group.Source!)));
        }

        foreach (var list in lists.GetAll())
        {
            var location = lists.GetLocation(list.Id);
            if (location is not null && IsUnder(location, listsRoot))
            {
                items.Add(new DataItem(
                    DataItemKind.TaskList,
                    Path.GetRelativePath(listsRoot, location),
                    location,
                    [list.Id],
                    SizeOf(location)));
            }
        }

        if (Directory.Exists(assetsRoot))
        {
            foreach (var file in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
            {
                items.Add(new DataItem(
                    DataItemKind.Asset, Path.GetRelativePath(assetsRoot, file), file, [], SizeOf(file)));
            }
        }

        return items
            .OrderBy(i => i.Kind)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? ReadText(DataItem item) =>
        GetItems().Any(i => i.Kind == item.Kind && i.Location == item.Location) && File.Exists(item.Location)
            ? File.ReadAllText(item.Location)
            : null;

    private static long? SizeOf(string file) => File.Exists(file) ? new FileInfo(file).Length : null;

    private static bool IsUnder(string file, string root)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
