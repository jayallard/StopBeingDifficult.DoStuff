namespace Sbd.DoStuff.Domain.Processes;

internal sealed class UnixProcessRunner(IEnumerable<string>? moduleDirectories = null)
    : ProcessRunnerBase(moduleDirectories)
{
    protected override (string FileName, string Arguments) BuildShellInvocation(string command, PowerShellEdition edition)
        => edition == PowerShellEdition.Windows
            ? throw new PowerShellNotFoundException(
                "Windows PowerShell (powershell.exe) is not available on this platform.")
            : ("/bin/sh", $"-c \"{command}\"");
}
