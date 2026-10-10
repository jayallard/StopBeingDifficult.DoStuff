using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class ListDetail
{
    [Parameter] public string ListId { get; set; } = "";

    private TaskListDefinition? _list;
    private CategoryNode? _root;
    private int _resetVersion;
    private bool _tabbed;
    private const string ViewModeKey = "sbd.listView";
    private string? _activeTab;

    // Falls back to the first top-level category when none is chosen yet.
    private CategoryNode? ActiveTab(List<CategoryNode> tabs) =>
        tabs.FirstOrDefault(t => t.Segment == _activeTab) ?? tabs.FirstOrDefault();

    protected override void OnParametersSet()
    {
        _list = ListRepository.Find(ListId);
        _root = _list is null ? null : CategoryTreeBuilder.Build(_list.Entries, Library);
    }

    // Browser storage isn't reachable until the circuit is interactive, so the saved mode is read after the first render.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            var saved = await JS.InvokeAsync<string?>("localStorage.getItem", ViewModeKey);
            var tab = await JS.InvokeAsync<string?>("localStorage.getItem", TabKey);
            if (saved == "tabbed" || tab is not null)
            {
                _tabbed = saved == "tabbed";
                _activeTab = tab;
                StateHasChanged();
            }
        }
        catch (JSException)
        {
        }
    }

    // Remembered per list so the run page's Back link returns to the tab the task was started from.
    private string TabKey => $"sbd.listTab.{ListId}";

    private async Task SelectTab(string segment)
    {
        _activeTab = segment;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", TabKey, segment);
        }
        catch (JSException)
        {
        }
    }

    private async Task SetTabbed(bool tabbed)
    {
        _tabbed = tabbed;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", ViewModeKey, tabbed ? "tabbed" : "list");
        }
        catch (JSException)
        {
        }
    }

    private void Reset()
    {
        if (_list is null)
        {
            return;
        }

        RunStore.ClearForList(ListId);
        _resetVersion++;
    }
}
