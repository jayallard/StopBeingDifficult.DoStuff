namespace Sbd.DoStuff.Domain.Processes;

internal sealed class UnixProcessRunner : ProcessRunnerBase
{
    protected override (string FileName, string Arguments) BuildShellInvocation(string command, bool useWindowsPowerShell)
        => ("/bin/sh", $"-c \"{command}\"");
}
