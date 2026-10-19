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

    private string[] _iconDirectories;
    private readonly string? _createDirectory;

    public TaskIconCatalog(IEnumerable<string> assetsDirectories, string? defaultIcon = null)
    {
        var assets = assetsDirectories.ToArray();
        _iconDirectories = assets
            .Select(directory => Path.GetFullPath(Path.Combine(directory, IconsFolder)))
            .Where(Directory.Exists)
            .ToArray();

        // New icons go in the first assets directory, alongside the shared (public) task data.
        _createDirectory = assets.Length > 0 ? Path.GetFullPath(Path.Combine(assets[0], IconsFolder)) : null;
        DefaultIcon = string.IsNullOrWhiteSpace(defaultIcon) ? FallbackDefaultIcon : defaultIcon.Trim();
    }

    /// <summary>Every icon file found, first directory first; a name in an earlier directory hides later ones.</summary>
    public IReadOnlyList<IconFile> GetAll()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var icons = new List<IconFile>();
        foreach (var directory in _iconDirectories)
        {
            foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    && seen.Add(Path.GetFileNameWithoutExtension(file)))
                {
                    icons.Add(new IconFile(Path.GetFileNameWithoutExtension(file), Path.GetFileName(file), file));
                }
            }
        }

        return icons;
    }

    /// <summary>The icon's name without a known image extension (<c>git.svg</c> becomes <c>git</c>).</summary>
    public static string NormalizeName(string icon)
    {
        var name = icon.Trim();
        return Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            ? name[..^Path.GetExtension(name).Length]
            : name;
    }

    /// <summary>True if the icon could be created: a plain file name, without folders or odd characters.</summary>
    public bool CanCreate(string icon)
    {
        var name = NormalizeName(icon);
        return _createDirectory is not null
            && name.Length > 0
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ' ')
            && !name.StartsWith('.');
    }

    /// <summary>
    /// Writes a placeholder SVG (coloured tile with the name's first letters) for <paramref name="icon"/>
    /// into the Icons folder of the first assets directory, to be replaced with real artwork later.
    /// </summary>
    public string Create(string icon)
    {
        var name = NormalizeName(icon);
        return Add(name, ".svg", System.Text.Encoding.UTF8.GetBytes(BuildPlaceholderSvg(name)));
    }

    /// <summary>Adds an image file (<c>.svg</c> or <c>.png</c>) under <paramref name="icon"/> in the first assets directory.</summary>
    public string Add(string icon, string extension, byte[] content)
    {
        if (!CanCreate(icon))
        {
            throw new ArgumentException($"Can't create an icon named '{icon}'.", nameof(icon));
        }

        if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Icons must be {string.Join(" or ", Extensions)} files.", nameof(extension));
        }

        var name = NormalizeName(icon);
        if (TryFindFile(name, out _))
        {
            throw new InvalidOperationException($"Icon '{name}' already exists.");
        }

        Directory.CreateDirectory(_createDirectory!);
        var path = Path.Combine(_createDirectory!, name + extension.ToLowerInvariant());
        File.WriteAllBytes(path, content);

        // The folder may not have existed when the catalog was built.
        if (!_iconDirectories.Contains(_createDirectory!, StringComparer.OrdinalIgnoreCase))
        {
            _iconDirectories = [.._iconDirectories, _createDirectory!];
        }

        return path;
    }

    /// <summary>Deletes the icon file. Tasks that still name it fall back to the default icon.</summary>
    public void Delete(string icon)
    {
        var name = NormalizeName(icon);
        if (name.Equals(DefaultIcon, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The default icon can't be deleted.");
        }

        if (!TryFindFile(name, out var path))
        {
            throw new InvalidOperationException($"Icon '{name}' doesn't exist.");
        }

        File.Delete(path);
    }

    private static string BuildPlaceholderSvg(string name)
    {
        var hue = name.Aggregate(17, (hash, c) => unchecked(hash * 31 + char.ToLowerInvariant(c))) % 360;
        var label = System.Security.SecurityElement.Escape(name.Length <= 3 ? name : name[..2]);
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" width="48" height="48">
              <rect width="48" height="48" rx="10" fill="hsl({Math.Abs(hue)}, 55%, 42%)"/>
              <text x="24" y="24" font-family="Segoe UI, Helvetica, Arial, sans-serif" font-size="20" font-weight="700" fill="#fff" text-anchor="middle" dominant-baseline="central">{label}</text>
            </svg>

            """;
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
