using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class TaskDetail
{
    [Parameter] public string ListId { get; set; } = "";
    [Parameter] public string TaskId { get; set; } = "";

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
            _entries.Add((entry, new TaskListEntryView(effective, values)));
        }
    }

    private void Run(TaskListEntryView view)
    {
        var task = TaskFactory.Create(view.Definition, view.ParameterValues);
        var run = Engine.StartRun(task, ListId);
        Navigation.NavigateTo($"lists/{ListId}/tasks/{view.Definition.Id}/runs/{run.RunId}");
    }
}
