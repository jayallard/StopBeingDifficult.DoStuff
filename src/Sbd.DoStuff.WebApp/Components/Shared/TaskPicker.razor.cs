using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class TaskPicker
{
    private const int MaxResults = 50;

    [Parameter, EditorRequired] public EventCallback<string> OnPicked { get; set; }

    private string _query = "";
    private bool _open;

    private IEnumerable<TaskDefinition> Matches => Library.GetAll()
        .Where(t => _query.Length == 0
            || t.Id.Contains(_query, StringComparison.OrdinalIgnoreCase)
            || t.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)
            || (t.Description?.Contains(_query, StringComparison.OrdinalIgnoreCase) ?? false))
        .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
        .Take(MaxResults);

    private void OnInput(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? "";
        _open = true;
    }

    private async Task Pick(TaskDefinition task)
    {
        _open = false;
        _query = "";
        await OnPicked.InvokeAsync(task.Id);
    }
}
