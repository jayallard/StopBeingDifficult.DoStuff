namespace Sbd.DoStuff.Domain.Processes;

internal sealed class WindowsProcessRunner : ProcessRunnerBase
{
    protected override (string FileName, string Arguments) BuildShellInvocation(string command)
        => ("cmd.exe", $"/c \"{command}\"");
}
