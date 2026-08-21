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

    private static string StatusClass(TaskRunStatus status) => status switch
    {
        TaskRunStatus.Succeeded => "font-medium text-emerald-600",
        TaskRunStatus.Failed => "font-medium text-red-600",
        TaskRunStatus.Cancelled => "font-medium text-amber-600",
        TaskRunStatus.Running => "font-medium text-sky-600",
        _ => "font-medium text-slate-600",
    };

    private static string ResultBadgeClass(int resultCode) => resultCode == 0
        ? "border border-emerald-200 bg-emerald-50 text-emerald-700"
        : "border border-red-200 bg-red-50 text-red-700";

    private static string LineClass(OutputStream stream) => stream switch
    {
        OutputStream.StandardError => "text-red-400",
        OutputStream.System => "italic text-slate-500",
        _ => "text-slate-100",
    };

    public void Dispose() => Engine.RunChanged -= OnRunChanged;
}
