using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Sbd.DoStuff.WebApp.Editing;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class OrganizerCategory
{
    [Parameter, EditorRequired] public OrganizerNode Node { get; set; } = null!;
    [Parameter, EditorRequired] public OrganizerModel Model { get; set; } = null!;

    /// <summary>Raised after the model changed in a way that must be persisted.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    private bool _addingTask;
    private bool _addingCategory;
    private string _newName = "";
    private string? _error;

    private async Task AddTask(string taskId)
    {
        _addingTask = false;
        Model.AddEntry(taskId, Node);
        await OnChanged.InvokeAsync();
    }

    private async Task AddSubcategory()
    {
        _error = Model.AddCategory(Node, _newName);
        if (_error is null)
        {
            _newName = "";
            _addingCategory = false;
            await OnChanged.InvokeAsync();
        }
    }

    private void CancelAddCategory()
    {
        _addingCategory = false;
        _newName = "";
        _error = null;
    }

    private async Task OnNewNameKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await AddSubcategory();
        }
        else if (e.Key == "Escape")
        {
            CancelAddCategory();
        }
    }

    private async Task Shift(EntryEditModel entry, int delta)
    {
        Model.MoveEntryBy(entry, Node, delta);
        await OnChanged.InvokeAsync();
    }

    // Every category except this one, and except any other that already holds the task.
    private IEnumerable<OrganizerNode> Destinations(EntryEditModel entry) =>
        Model.AllCategories().Where(c => !ReferenceEquals(c, Node) && !c.Entries.Contains(entry));

    private async Task MoveTo(EntryEditModel entry, string nodeId)
    {
        if (Model.Find(nodeId) is { } target)
        {
            Model.MoveEntry(entry, Node, target, target.Entries.Count);
            await OnChanged.InvokeAsync();
        }
    }

    private async Task Clone(EntryEditModel entry)
    {
        Model.CloneEntry(entry, Node);
        await OnChanged.InvokeAsync();
    }

    private async Task Delete(EntryEditModel entry)
    {
        Model.RemoveEntry(entry, Node);
        await OnChanged.InvokeAsync();
    }
}
