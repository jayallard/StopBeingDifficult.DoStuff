using Sbd.DoStuff.WebApp.Services;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class CommandBanner
{
    protected override void OnInitialized() => Commands.Changed += OnChanged;

    private void OnChanged() => InvokeAsync(StateHasChanged);

    private static string Message(PendingCommand command) => command.Name switch
    {
        PendingCommands.RestartApplication => "A task asked for the app to be restarted. You can do that from the Configuration page.",
        PendingCommands.RestartComputer => "A task asked for the computer to be restarted.",
        _ => command.Name,
    };

    public void Dispose() => Commands.Changed -= OnChanged;
}
