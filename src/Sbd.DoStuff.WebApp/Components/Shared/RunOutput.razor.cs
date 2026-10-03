using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Execution;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class RunOutput
{
    [Parameter, EditorRequired] public TaskRun Run { get; set; } = null!;
    [Parameter] public bool ShowStatus { get; set; }

    protected override void OnInitialized() => Engine.RunChanged += OnRunChanged;

    private void OnRunChanged(TaskRun run)
    {
        if (run.RunId == Run.RunId)
        {
            InvokeAsync(StateHasChanged);
        }
    }

    internal static string StatusClass(TaskRunStatus status) => status switch
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

    private IEnumerable<TaskOutputLine> MessageLines =>
        Run.OutputLines.Where(l => l.Text.StartsWith(':') || l.Text.StartsWith('!'));

    private static string LineClass(TaskOutputLine line) => line.Text.StartsWith('!')
        ? "text-red-400"
        : line.Stream switch
        {
            OutputStream.StandardError => "text-red-400",
            OutputStream.System => "italic text-slate-500",
            _ => "text-slate-100",
        };

    private static string LineText(TaskOutputLine line) => line.Text.StartsWith('!')
        ? line.Text[1..]
        : line.Text;

    public void Dispose() => Engine.RunChanged -= OnRunChanged;
}
