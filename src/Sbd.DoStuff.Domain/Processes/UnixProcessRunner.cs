namespace Sbd.DoStuff.Domain.Processes;

internal sealed class UnixProcessRunner(IEnumerable<string>? moduleDirectories = null)
    : ProcessRunnerBase(moduleDirectories)
{
    protected override (string FileName, string Arguments) BuildShellInvocation(string command, bool useWindowsPowerShell)
        => ("/bin/sh", $"-c \"{command}\"");
}
