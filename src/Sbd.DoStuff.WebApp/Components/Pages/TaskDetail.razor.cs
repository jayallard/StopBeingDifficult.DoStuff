using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class TaskDetail
{
    [Parameter] public string ListId { get; set; } = "";
    [Parameter] public string TaskId { get; set; } = "";

    private string? _runError;
    private TaskListDefinition? _list;
    private EffectiveTaskDefinition? _definition;
    private List<(TaskListEntry Entry, TaskListEntryView View)> _entries = [];

    protected override void OnParametersSet()
    {
        _list = ListRepository.Find(ListId);
        _entries = [];
        _definition = null;

        if (_list is null)
        {
            return;
        }

        var definition = Library.Find(TaskId);
        if (definition is null)
        {
            return;
        }

        foreach (var entry in _list.Entries.Where(e => e.TaskId == TaskId))
        {
            var effective = TaskDefinitionResolver.Resolve(definition, Library);
            var values = TaskParameterResolver.Resolve(effective, entry.ParameterValues);
            _definition = effective;
            _entries.Add((entry, new TaskListEntryView(effective, values, entry.Notes, entry.Name)));
        }
    }

    private void Run(TaskListEntryView view)
    {
        _runError = null;

        try
        {
            var task = TaskFactory.Create(view.Definition, view.ParameterValues);
            var run = Engine.StartRun(task, ListId);
            Navigation.NavigateTo($"lists/{ListId}/tasks/{view.Definition.Id}/runs/{run.RunId}");
        }
        catch (Exception ex)
        {
            _runError = ex.Message;
        }
    }
}
