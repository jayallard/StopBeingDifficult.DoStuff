using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.WebApp.Editing;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class ListEdit
{
    [Parameter] public string? ListId { get; set; }

    private ListEditModel _model = new();
    private string? _loadedFor;
    private bool _notFound;
    private readonly List<string> _errors = [];
    private string _pickCategory = "";
    private string _newCategory = "";
    private string? _selectedTaskId;

    private ElementReference _treeRoot;
    private IJSObjectReference? _sortable;
    private DotNetObjectReference<ListEdit>? _self;

    /// <summary>Path of the top-level category currently being edited; the editor shows one at a time.</summary>
    private string? _activeCategory;

    // Falls back to the first top-level category when none is chosen or the chosen one no longer exists (e.g. renamed).
    private EditCategoryNode? ActiveNode(EditCategoryNode tree) =>
        _activeCategory is not null && tree.Children.TryGetValue(_activeCategory, out var node)
            ? node
            : tree.Children.Values.FirstOrDefault();

    private static int CountEntries(EditCategoryNode node) =>
        node.Entries.Count + node.Children.Values.Sum(CountEntries);

    private void ShowCategoryOf(string category) => _activeCategory = category.Split('.')[0];

    private bool IsNew => ListId is null;
    private string? SelectedTaskName => _selectedTaskId is null ? null : Library.Find(_selectedTaskId)?.Name ?? _selectedTaskId;
    private string CancelHref => IsNew ? "" : $"lists/{ListId}";

    protected override void OnParametersSet()
    {
        if (_loadedFor == ListId)
        {
            return;
        }

        _loadedFor = ListId;
        _errors.Clear();

        if (IsNew)
        {
            _notFound = false;
            _model = new ListEditModel();
            return;
        }

        var list = ListRepository.Find(ListId!);
        _notFound = list is null;
        if (list is not null)
        {
            _model = ListEditModel.From(list, ListRepository.GetScope(list.Id) ?? TaskListScope.Private);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_notFound)
        {
            return;
        }

        _sortable ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/sortable-interop.js");
        _self ??= DotNetObjectReference.Create(this);
        await _sortable.InvokeVoidAsync("init", _treeRoot, _self);
    }

    /// <summary>Called from sortable-interop.js after a drop; the DOM move was reverted there, so just update the model.</summary>
    [JSInvokable]
    public async Task OnReorder(string entryKey, string fromPath, string toPath, int newIndex)
    {
        var entry = _model.Entries.FirstOrDefault(e => e.Key.ToString() == entryKey);
        if (entry is null)
        {
            return;
        }

        _model.Move(entry, fromPath, toPath, newIndex);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>A category dropped on the tree background (not on another category) becomes top-level.</summary>
    private void OnDropTopLevel()
    {
        var dragged = _model.DraggedCategory;
        _model.DraggedCategory = null;
        if (dragged is not null)
        {
            _model.MoveCategory(dragged, "");
        }
    }

    // With a category chosen in the drop-down the task is added straight away; otherwise it becomes the
    // selected task, to be filed with a category's + Add button.
    private void AddTask(string taskId)
    {
        _errors.Clear();
        var category = _pickCategory.Trim();
        if (category.Length == 0)
        {
            _selectedTaskId = taskId;
            return;
        }

        if (!TaskListValidator.IsValidCategory(category))
        {
            _errors.Add("Enter a valid category (like cleanup.temp), or clear it and use a category's + Add button.");
            return;
        }

        _model.AddEntry(taskId, category);
        ShowCategoryOf(category);
    }

    private void AddSelectedTo(string category)
    {
        if (_selectedTaskId is not null)
        {
            _errors.Clear();
            _model.AddEntry(_selectedTaskId, category);
        }
    }

    private void AddCategory()
    {
        var category = _newCategory.Trim();
        if (!TaskListValidator.IsValidCategory(category))
        {
            return;
        }

        _model.AddCategory(category);
        ShowCategoryOf(category);
        _newCategory = "";
    }

    private void OnNewCategoryKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            AddCategory();
        }
    }

    private void Save()
    {
        _errors.Clear();

        if (string.IsNullOrWhiteSpace(_model.Id))
        {
            _model.Id = Slug(_model.Name);
        }

        var definition = _model.ToDefinition();
        _errors.AddRange(TaskListValidator.Validate(definition, Library));
        if (_errors.Count > 0)
        {
            return;
        }

        try
        {
            ListRepository.Save(definition, _model.Scope, ListId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _errors.Add(ex.Message);
            return;
        }

        Navigation.NavigateTo($"lists/{definition.Id}");
    }

    private static string Slug(string name) =>
        Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    public async ValueTask DisposeAsync()
    {
        if (_sortable is not null)
        {
            try
            {
                await _sortable.InvokeVoidAsync("dispose", _treeRoot);
                await _sortable.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _self?.Dispose();
    }
}
