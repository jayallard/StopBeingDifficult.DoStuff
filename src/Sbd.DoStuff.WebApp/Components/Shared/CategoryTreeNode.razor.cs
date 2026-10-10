using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class CategoryTreeNode : IDisposable
{
    [Parameter, EditorRequired] public CategoryNode Node { get; set; } = null!;
    [Parameter, EditorRequired] public string ListId { get; set; } = "";

    private readonly Dictionary<string, TaskRun> _lastRuns = new();
    private readonly Dictionary<string, string> _runErrors = new();

    protected override void OnInitialized()
    {
        foreach (var task in Node.Tasks)
        {
            // A list can hold the same definition several times with different parameter values, so the
            // last run is tracked per entry, not per definition id. Runs made before entries were keyed
            // have no EntryKey and are ignored.
            var key = KeyOf(task);
            var lastRun = RunStore.GetRecentForTask(ListId, task.Definition.Id, int.MaxValue)
                .FirstOrDefault(r => r.EntryKey == key);
            if (lastRun is not null)
            {
                _lastRuns[key] = lastRun;
            }
        }

        Engine.RunChanged += OnRunChanged;
    }

    private void OnRunChanged(TaskRun run)
    {
        if (run.ListId == ListId && run.EntryKey is not null && Node.Tasks.Any(t => KeyOf(t) == run.EntryKey))
        {
            _lastRuns[run.EntryKey] = run;
            InvokeAsync(StateHasChanged);
        }
    }

    private void Run(TaskListEntryView view)
    {
        _runErrors.Remove(view.Definition.Id);

        try
        {
            var task = TaskFactory.Create(view.Definition, view.ParameterValues);
            var run = Engine.StartRun(task, ListId, KeyOf(view));
            Navigation.NavigateTo($"lists/{ListId}/tasks/{view.Definition.Id}/runs/{run.RunId}");
        }
        catch (Exception ex)
        {
            _runErrors[view.Definition.Id] = ex.Message;
        }
    }

    private static string KeyOf(TaskListEntryView view) => TaskRunKey.For(view.Definition.Id, view.ParameterValues);

    private string RowClass(TaskListEntryView view)
    {
        const string baseClass = "flex items-center justify-between rounded border px-3 py-2 transition";

        if (!_lastRuns.TryGetValue(KeyOf(view), out var lastRun) || lastRun.Status != TaskRunStatus.Succeeded && lastRun.Status != TaskRunStatus.Failed)
        {
            return $"{baseClass} border-slate-200 bg-white hover:border-indigo-300";
        }

        return lastRun.ResultCode == 0
            ? $"{baseClass} border-emerald-300 bg-emerald-50 hover:border-emerald-400"
            : $"{baseClass} border-red-300 bg-red-50 hover:border-red-400";
    }

    public void Dispose() => Engine.RunChanged -= OnRunChanged;
}
