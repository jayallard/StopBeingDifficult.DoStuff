namespace Sbd.DoStuff.Domain.Icons;

/// <summary>
/// Finds task icons: image files in the <c>Icons</c> folder of any assets directory (first match
/// wins). An icon is referred to by file name, with or without extension (<c>git</c> or
/// <c>git.svg</c>); without one, <c>.svg</c> then <c>.png</c> is tried. SVG is the house format:
/// small text files that scale and are easy to diff. The default icon is the library-level
/// fallback for any task whose own icon is unset or missing.
/// </summary>
public sealed class TaskIconCatalog
{
    public const string IconsFolder = "Icons";
    public const string FallbackDefaultIcon = "default";

    private static readonly string[] Extensions = [".svg", ".png"];

    private readonly string[] _iconDirectories;

    public TaskIconCatalog(IEnumerable<string> assetsDirectories, string? defaultIcon = null)
    {
        _iconDirectories = assetsDirectories
            .Select(directory => Path.GetFullPath(Path.Combine(directory, IconsFolder)))
            .Where(Directory.Exists)
            .ToArray();
        DefaultIcon = string.IsNullOrWhiteSpace(defaultIcon) ? FallbackDefaultIcon : defaultIcon.Trim();
    }

    /// <summary>The library-level icon used when a task has none (or its icon doesn't exist).</summary>
    public string DefaultIcon { get; }

    /// <summary>The icon to show for <paramref name="icon"/>: itself if it exists, otherwise the default.</summary>
    public string Resolve(string? icon) => icon is not null && TryFindFile(icon, out _) ? icon : DefaultIcon;

    public bool TryFindFile(string icon, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(icon) || Path.IsPathRooted(icon))
        {
            return false;
        }

        var hasExtension = Extensions.Contains(Path.GetExtension(icon), StringComparer.OrdinalIgnoreCase);
        var candidates = hasExtension ? [icon] : Extensions.Select(extension => icon + extension).ToArray();

        foreach (var directory in _iconDirectories)
        {
            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(Path.Combine(directory, candidate));

                // Icon names come from YAML and the URL, so keep them from escaping the folder.
                if (full.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                {
                    path = full;
                    return true;
                }
            }
        }

        return false;
    }
}
