using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class TaskRunView
{
    [Parameter] public string ListId { get; set; } = "";
    [Parameter] public string TaskId { get; set; } = "";
    [Parameter] public Guid RunId { get; set; }

    private TaskRun? _run;
    private TaskListEntryView? _entry;

    private string BackHref => $"lists/{ListId}";

    private string DisplayName =>
        !string.IsNullOrWhiteSpace(_entry?.Name) ? _entry.Name
        : _entry?.Definition.Name ?? _run?.TaskName ?? "Run not found";

    protected override void OnInitialized()
    {
        _run = RunStore.Get(RunId);
        _entry = FindEntry();
        Engine.RunChanged += OnRunChanged;
    }

    // Maps the run back to the list entry that started it (by EntryKey), falling back to the
    // definition alone for runs without a matching entry (e.g. test runs, or the list was edited).
    private TaskListEntryView? FindEntry()
    {
        if (_run is null)
        {
            return null;
        }

        try
        {
            var definition = Library.Find(_run.TaskId);
            if (definition is null)
            {
                return null;
            }

            var effective = TaskDefinitionResolver.Resolve(definition, Library);
            var list = ListRepository.Find(_run.ListId);
            foreach (var entry in list?.Entries.Where(e => e.TaskId == _run.TaskId) ?? [])
            {
                var values = TaskParameterResolver.Resolve(effective, entry.ParameterValues);
                if (TaskRunKey.For(effective.Id, values) == _run.EntryKey)
                {
                    return new TaskListEntryView(effective, values, entry.Notes, entry.Name);
                }
            }

            return new TaskListEntryView(effective, effective.PinnedParameterValues);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnRunChanged(TaskRun run)
    {
        if (run.RunId == RunId)
        {
            InvokeAsync(StateHasChanged);
        }
    }

    private void Cancel() => Engine.TryCancelRun(RunId);

    public void Dispose() => Engine.RunChanged -= OnRunChanged;
}
