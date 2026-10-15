using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.WebApp.Services;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class RunOutput
{
    [Parameter, EditorRequired] public TaskRun Run { get; set; } = null!;
    [Parameter] public bool ShowStatus { get; set; }

    private ElementReference _output;
    private IJSObjectReference? _autoscroll;

    protected override void OnInitialized() => Engine.RunChanged += OnRunChanged;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _autoscroll = await JS.InvokeAsync<IJSObjectReference>("import", "./js/autoscroll.js");
        await _autoscroll.InvokeVoidAsync("attach", _output);
    }

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
        Run.OutputLines.Where(l => !PendingCommands.IsCommandLine(l.Text) && (l.Text.StartsWith(':') || l.Text.StartsWith('!')));

    private static string LineClass(TaskOutputLine line) => PendingCommands.IsCommandLine(line.Text)
        ? "font-medium text-amber-300"
        : line.Text.StartsWith('!')
        ? "text-red-400"
        : line.Stream switch
        {
            OutputStream.StandardError => "text-red-400",
            OutputStream.System => "italic text-slate-500",
            _ => "text-slate-100",
        };

    private static string LineText(TaskOutputLine line) => PendingCommands.IsCommandLine(line.Text)
        ? $"Command: {PendingCommands.CommandName(line.Text)}"
        : line.Text.StartsWith('!')
        ? line.Text[1..]
        : line.Text;

    public async ValueTask DisposeAsync()
    {
        Engine.RunChanged -= OnRunChanged;

        if (_autoscroll is not null)
        {
            try
            {
                await _autoscroll.InvokeVoidAsync("detach", _output);
                await _autoscroll.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
