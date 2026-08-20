namespace Sbd.DoStuff.Domain.Processes;

internal sealed class UnixProcessRunner : ProcessRunnerBase
{
    protected override (string FileName, string Arguments) BuildShellInvocation(string command)
        => ("/bin/sh", $"-c \"{command}\"");
}
