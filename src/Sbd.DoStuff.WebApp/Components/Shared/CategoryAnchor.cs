using System.Text.RegularExpressions;

namespace Sbd.DoStuff.WebApp.Components.Shared;

/// <summary>Builds the DOM id used to link the list outline to a category section.</summary>
public static partial class CategoryAnchor
{
    // Dots separate path segments, so "--" keeps a parent id a prefix of its children's ids.
    public static string Id(string fullPath) =>
        "cat-" + string.Join("--", fullPath.Split('.').Select(s => Slug().Replace(s.ToLowerInvariant(), "-").Trim('-')));

    // Prefixed with the category id so tab lookup by id prefix also finds a task's tab.
    public static string TaskId(string fullPath, int index) => $"{Id(fullPath)}--task-{index}";

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Slug();
}
