using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.WebApp.Editing;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class ListOrganize
{
    [Parameter] public string ListId { get; set; } = "";

    private TaskListDefinition? _list;
    private string _storageDirectory = "";
    private OrganizerModel _model = new();
    private string? _loadedFor;
    private string? _error;
    private string _newCategory = "";
    private string? _newCategoryError;

    private ElementReference _root;
    private IJSObjectReference? _sortable;
    private DotNetObjectReference<ListOrganize>? _self;

    protected override void OnParametersSet()
    {
        if (_loadedFor == ListId)
        {
            return;
        }

        _loadedFor = ListId;
        _error = null;
        _list = ListRepository.Find(ListId);
        if (_list is not null)
        {
            _storageDirectory = ListRepository.GetStorageDirectory(ListId) ?? ListRepository.StorageDirectories[0];
            _model = OrganizerModel.From(_list);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_list is null)
        {
            return;
        }

        _sortable ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/organizer-interop.js");
        _self ??= DotNetObjectReference.Create(this);
        await _sortable.InvokeVoidAsync("init", _root, _self);
    }

    /// <summary>Called from organizer-interop.js after a task drop; the DOM move was reverted there.</summary>
    [JSInvokable]
    public async Task OnMoveTask(string entryKey, string fromId, string toId, int newIndex)
    {
        var from = _model.Find(fromId);
        var to = _model.Find(toId);
        var entry = from?.Entries.FirstOrDefault(e => e.Key.ToString() == entryKey);
        if (from is not null && to is not null && entry is not null)
        {
            _model.MoveEntry(entry, from, to, newIndex);
            Persist();
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Called from organizer-interop.js after a category drop; the DOM move was reverted there.</summary>
    [JSInvokable]
    public async Task OnMoveCategory(string nodeId, string parentId, int newIndex)
    {
        var node = _model.Find(nodeId);
        var parent = _model.Find(parentId);
        if (node is not null && parent is not null)
        {
            _error = _model.MoveCategory(node, parent, newIndex);
            if (_error is null)
            {
                Persist();
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    private void OnChanged() => Persist();

    private void AddTopLevelCategory()
    {
        _newCategoryError = _model.AddCategory(_model.Root, _newCategory);
        if (_newCategoryError is null)
        {
            _newCategory = "";
        }
    }

    private void OnNewCategoryKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            AddTopLevelCategory();
        }
    }

    // Every change is written straight to the list file, so there is no unsaved state to lose.
    private void Persist()
    {
        _error = null;
        var definition = _list! with { Entries = _model.ToEntries() };

        var problems = TaskListValidator.Validate(definition, Library);
        if (problems.Count > 0)
        {
            _error = "Not saved: " + string.Join(" ", problems);
            return;
        }

        try
        {
            ListRepository.Save(definition, _storageDirectory, ListId);
            _list = definition;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _error = "Not saved: " + ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_sortable is not null)
        {
            try
            {
                await _sortable.InvokeVoidAsync("dispose", _root);
                await _sortable.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _self?.Dispose();
    }
}
