using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.WebApp.Components.Shared;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class ListDetail : IDisposable
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

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    // In tabbed mode an outline link may target a category on an inactive tab: switch to it, then scroll.
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) =>
        _ = HandleFragment(new Uri(e.Location).Fragment.TrimStart('#'));

    // Blazor navigates same-page hash links with pushState (no LocationChanged or hashchange), so link clicks are caught in JS.
    [JSInvokable]
    public Task OnHashChanged(string fragment) => HandleFragment(fragment.TrimStart('#'));

    private async Task HandleFragment(string fragment)
    {
        if (!_tabbed || _root is null || !fragment.StartsWith("cat-"))
        {
            return;
        }

        var tab = _root.Children.Values.FirstOrDefault(t =>
        {
            var id = CategoryAnchor.Id(t.FullPath);
            return fragment == id || fragment.StartsWith(id + "--");
        });
        if (tab is null)
        {
            return;
        }

        _pendingScroll = fragment;
        await InvokeAsync(async () =>
        {
            await SelectTab(tab.Segment);
            StateHasChanged();
        });
    }

    private string? _pendingScroll;

    private DotNetObjectReference<ListDetail>? _self;

    public void Dispose()
    {
        Navigation.LocationChanged -= OnLocationChanged;
        _self?.Dispose();
    }

    protected override void OnParametersSet()
    {
        _list = ListRepository.Find(ListId);
        _root = _list is null ? null : CategoryTreeBuilder.Build(_list.Entries, Library);
    }

    // After an inline edit was saved: rebuild the tree from the stored list; the new key re-creates the nodes with fresh run state.
    private void Reload()
    {
        OnParametersSet();
        _resetVersion++;
    }

    // Browser storage isn't reachable until the circuit is interactive, so the saved mode is read after the first render.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingScroll is not null)
        {
            var id = _pendingScroll;
            _pendingScroll = null;
            try
            {
                await JS.InvokeVoidAsync("eval", $"document.getElementById('{id}')?.scrollIntoView()");
            }
            catch (JSException)
            {
            }
        }

        if (!firstRender)
        {
            return;
        }

        _self = DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("eval",
                "window.sbdHashListen=(r)=>{if(window.sbdHashHandler)document.removeEventListener('click',window.sbdHashHandler);" +
                "window.sbdHashHandler=(e)=>{const a=e.target.closest&&e.target.closest('a[href*=\"#cat-\"]');" +
                "if(!a)return;const id=new URL(a.href).hash.slice(1);" +
                "r.invokeMethodAsync('OnHashChanged','#'+id).finally(()=>setTimeout(()=>{" +
                "const el=document.getElementById(id);" +
                "if(el){let d=el;while(d){if(d.tagName==='DETAILS')d.open=true;d=d.parentElement}el.scrollIntoView()}" +
                "if(el&&id.includes('--task-'))el.animate([{boxShadow:'0 0 0 3px #818cf8',backgroundColor:'#e0e7ff'},{boxShadow:'0 0 0 3px transparent'}],{duration:1500,easing:'ease-out'})},150))};" +
                "document.addEventListener('click',window.sbdHashHandler)}");
            await JS.InvokeVoidAsync("sbdHashListen", _self);
        }
        catch (JSException)
        {
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
