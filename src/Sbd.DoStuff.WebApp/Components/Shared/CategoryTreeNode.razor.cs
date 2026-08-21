using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class CategoryTreeNode : IDisposable
{
    [Parameter, EditorRequired] public CategoryNode Node { get; set; } = null!;
    [Parameter, EditorRequired] public string ListId { get; set; } = "";

    private readonly Dictionary<string, TaskRun> _lastRuns = new();

    protected override void OnInitialized()
    {
        foreach (var task in Node.Tasks)
        {
            var lastRun = RunStore.GetRecentForTask(task.Definition.Id, 1).FirstOrDefault();
            if (lastRun is not null)
            {
                _lastRuns[task.Definition.Id] = lastRun;
            }
        }

        Engine.RunChanged += OnRunChanged;
    }

    private void OnRunChanged(TaskRun run)
    {
        if (Node.Tasks.Any(t => t.Definition.Id == run.TaskId))
        {
            _lastRuns[run.TaskId] = run;
            InvokeAsync(StateHasChanged);
        }
    }

    private void Run(TaskListEntryView view)
    {
        var task = TaskFactory.Create(view.Definition, view.ParameterValues);
        var run = Engine.StartRun(task);
        Navigation.NavigateTo($"lists/{ListId}/tasks/{view.Definition.Id}/runs/{run.RunId}");
    }

    private string RowClass(TaskListEntryView view)
    {
        const string baseClass = "flex items-center justify-between rounded border px-3 py-2 transition";

        if (!_lastRuns.TryGetValue(view.Definition.Id, out var lastRun) || lastRun.Status != TaskRunStatus.Succeeded && lastRun.Status != TaskRunStatus.Failed)
        {
            return $"{baseClass} border-slate-200 bg-white hover:border-indigo-300";
        }

        return lastRun.ResultCode == 0
            ? $"{baseClass} border-emerald-300 bg-emerald-50 hover:border-emerald-400"
            : $"{baseClass} border-red-300 bg-red-50 hover:border-red-400";
    }

    public void Dispose() => Engine.RunChanged -= OnRunChanged;
}
