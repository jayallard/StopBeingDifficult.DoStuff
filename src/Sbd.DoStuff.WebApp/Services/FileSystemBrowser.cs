namespace Sbd.DoStuff.WebApp.Services;

public sealed class FileSystemBrowser : IFileSystemBrowser
{
    public DirectoryListing List(string? path)
    {
        var folder = ResolveFolder(path);
        if (folder is null)
        {
            // No usable path: the drive list on Windows, the file system root elsewhere.
            return OperatingSystem.IsWindows() ? ListDrives() : List("/");
        }

        var parent = Directory.GetParent(folder)?.FullName;
        if (parent is null && OperatingSystem.IsWindows())
        {
            parent = string.Empty; // a drive root goes up to the drive list
        }

        try
        {
            var items = new DirectoryInfo(folder)
                .EnumerateFileSystemInfos()
                .OrderBy(e => e is not DirectoryInfo)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new FileSystemItem(e.Name, e.FullName, e is DirectoryInfo))
                .ToList();
            return new DirectoryListing(folder, parent, items);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new DirectoryListing(folder, parent, [], ex.Message);
        }
    }

    private static string? ResolveFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (File.Exists(full))
        {
            return Path.GetDirectoryName(full);
        }

        // Walk up to the nearest folder that exists.
        for (var dir = full; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (Directory.Exists(dir))
            {
                return dir;
            }
        }

        return null;
    }

    private static DirectoryListing ListDrives()
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new FileSystemItem(d.Name, d.RootDirectory.FullName, true))
            .ToList();
        return new DirectoryListing(string.Empty, null, drives);
    }
}
