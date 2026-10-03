using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Execution;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class TaskRunView
{
    [Parameter] public string ListId { get; set; } = "";
    [Parameter] public string TaskId { get; set; } = "";
    [Parameter] public Guid RunId { get; set; }

    private TaskRun? _run;

    private string BackHref => $"lists/{ListId}";

    protected override void OnInitialized()
    {
        _run = RunStore.Get(RunId);
        Engine.RunChanged += OnRunChanged;
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
