using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.WebApp.Editing;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class EditorCategoryNode
{
    [Parameter, EditorRequired] public EditCategoryNode Node { get; set; } = null!;
    [Parameter, EditorRequired] public ListEditModel Model { get; set; } = null!;
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>The task currently chosen in the picker, if any; the + Add button files it under this category.</summary>
    [Parameter] public string? SelectedTaskId { get; set; }
    [Parameter] public string? SelectedTaskName { get; set; }
    [Parameter] public EventCallback<string> OnAddSelected { get; set; }

    private bool _renaming;
    private string _renameText = "";
    private string? _renameError;

    private ElementReference _renameInput;
    private bool _focusRename;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusRename)
        {
            _focusRename = false;
            await _renameInput.FocusAsync();
        }
    }

    private void StartRename()
    {
        _renameText = Node.Path;
        _renameError = null;
        _renaming = true;
        _focusRename = true;
    }

    private async Task OnRenameKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await CommitRename();
        }
        else if (e.Key == "Escape")
        {
            _renaming = false;
        }
    }

    private async Task CommitRename()
    {
        var newPath = _renameText.Trim();
        if (!TaskListValidator.IsValidCategory(newPath))
        {
            _renameError = "Use a dot-separated path like cleanup.temp.";
            return;
        }

        _renaming = false;
        if (newPath != Node.Path)
        {
            Model.RenameCategory(Node.Path, newPath);
            await OnChanged.InvokeAsync();
        }
    }
}
