using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Sbd.DoStuff.Domain.DataSources;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.WebApp.Components.Pages;

namespace Sbd.DoStuff.WebApp.Components.Layout;

public partial class NavMenu : IDisposable
{
    private static readonly Regex ListPage = new(@"^lists/(?<id>[^/?#]+)/?(?:[?#].*)?$", RegexOptions.IgnoreCase);

    private sealed record ConfigurationOutlineItem(string Title, string Anchor, IReadOnlyList<ConfigurationOutlineItem> Children);

    private IReadOnlyList<ConfigurationOutlineItem>? _configurationOutline;
    private CategoryNode? _outlineRoot;
    private string _outlineListId = "";
    private string _outlineName = "";

    protected override void OnInitialized()
    {
        Navigation.LocationChanged += OnLocationChanged;
        Favorites.Changed += OnFavoritesChanged;
        BuildOutline();
    }

    private void OnFavoritesChanged() => InvokeAsync(StateHasChanged);

    // Favorites whose list no longer exists are skipped.
    private IEnumerable<TaskListDefinition> FavoriteListItems() =>
        ListRepository.GetAll().Where(l => Favorites.IsFavorite(l.Id)).OrderBy(l => l.Name);

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        BuildOutline();
        InvokeAsync(StateHasChanged);
    }

    // The outline is shown only while a list's own page is open; edit/organize/task pages leave the nav plain.
    private void BuildOutline()
    {
        _outlineRoot = null;
        _configurationOutline = null;

        var path = Navigation.ToBaseRelativePath(Navigation.Uri);
        if (Regex.IsMatch(path, @"^configuration/?(?:[?#].*)?$", RegexOptions.IgnoreCase))
        {
            _configurationOutline = DataSources.Select((source, index) => new ConfigurationOutlineItem(
                    source.Location,
                    Configuration.SourceAnchor(index),
                    source.GetItems().Select(i => i.Kind).Distinct().Order()
                        .Select(kind => new ConfigurationOutlineItem(
                            Configuration.KindTitle(kind), Configuration.KindAnchor(index, kind), []))
                        .ToList()))
                .ToList();
            return;
        }

        var match = ListPage.Match(path);
        if (!match.Success)
        {
            return;
        }

        var list = ListRepository.Find(Uri.UnescapeDataString(match.Groups["id"].Value));
        if (list is null)
        {
            return;
        }

        _outlineListId = list.Id;
        _outlineName = list.Name;
        _outlineRoot = CategoryTreeBuilder.Build(list.Entries, Library);
    }

    public void Dispose()
    {
        Navigation.LocationChanged -= OnLocationChanged;
        Favorites.Changed -= OnFavoritesChanged;
    }
}
