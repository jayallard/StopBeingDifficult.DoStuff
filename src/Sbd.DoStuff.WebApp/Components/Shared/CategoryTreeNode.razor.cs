using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class CategoryTreeNode
{
    [Parameter, EditorRequired] public CategoryNode Node { get; set; } = null!;
    [Parameter, EditorRequired] public string ListId { get; set; } = "";

    private void Run(TaskListEntryView view)
    {
        var task = TaskFactory.Create(view.Definition, view.ParameterValues);
        var run = Engine.StartRun(task);
        Navigation.NavigateTo($"lists/{ListId}/tasks/{view.Definition.Id}/runs/{run.RunId}");
    }
}
