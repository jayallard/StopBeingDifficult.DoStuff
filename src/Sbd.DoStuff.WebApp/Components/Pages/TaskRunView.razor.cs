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

    private Timer? _ticker;

    private bool IsActive => _run?.Status is TaskRunStatus.Pending or TaskRunStatus.Running;

    private string RunTime
    {
        get
        {
            if (_run is null)
            {
                return "";
            }

            var end = IsActive ? DateTimeOffset.UtcNow : _run.CompletedAt ?? _run.StartedAt;
            var elapsed = end - _run.StartedAt;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}:{elapsed:mm\\:ss}"
                : $"{elapsed:mm\\:ss}";
        }
    }

    protected override void OnInitialized() => Engine.RunChanged += OnRunChanged;

    // Also runs when a rerun navigates to the new run's URL, which reuses this component instance.
    protected override void OnParametersSet()
    {
        _run = RunStore.Get(RunId);
        _entry = FindEntry();
        _rerunError = null;
        UpdateTicker();
    }

    private string? _rerunError;

    private void Rerun()
    {
        if (_entry is null)
        {
            return;
        }

        try
        {
            var task = TaskFactory.Create(_entry.Definition, _entry.ParameterValues);
            var run = Engine.StartRun(task, _run!.ListId, TaskRunKey.For(_entry.Definition.Id, _entry.ParameterValues));
            Navigation.NavigateTo($"lists/{run.ListId}/tasks/{TaskId}/runs/{run.RunId}");
        }
        catch (Exception ex)
        {
            _rerunError = ex.Message;
        }
    }

    // Re-renders once a second while the run is active so the Run Time counter advances;
    // the timer is stopped when the run reaches a terminal state, freezing the displayed time.
    private void UpdateTicker()
    {
        if (IsActive)
        {
            _ticker ??= new Timer(_ => InvokeAsync(StateHasChanged), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        else if (_ticker is not null)
        {
            _ticker.Dispose();
            _ticker = null;
        }
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
            UpdateTicker();
            InvokeAsync(StateHasChanged);
        }
    }

    private void Cancel() => Engine.TryCancelRun(RunId);

    public void Dispose()
    {
        Engine.RunChanged -= OnRunChanged;
        _ticker?.Dispose();
    }
}
