using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Layout;

public partial class NavMenu : IDisposable
{
    private static readonly Regex ListPage = new(@"^lists/(?<id>[^/?#]+)/?(?:[?#].*)?$", RegexOptions.IgnoreCase);

    private CategoryNode? _outlineRoot;
    private string _outlineListId = "";
    private string _outlineName = "";

    protected override void OnInitialized()
    {
        Navigation.LocationChanged += OnLocationChanged;
        BuildOutline();
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        BuildOutline();
        InvokeAsync(StateHasChanged);
    }

    // The outline is shown only while a list's own page is open; edit/organize/task pages leave the nav plain.
    private void BuildOutline()
    {
        _outlineRoot = null;

        var match = ListPage.Match(Navigation.ToBaseRelativePath(Navigation.Uri));
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

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;
}
