using System.Text;

namespace Sbd.DoStuff.Domain.Processes;

internal sealed class WindowsProcessRunner : ProcessRunnerBase
{
    // powershell.exe serializes the error/warning/verbose/etc. streams to CLIXML instead of
    // plain text whenever its native stderr handle is redirected (as it always is here) — so
    // Write-Error/Write-Warning output would otherwise show up as unreadable "#< CLIXML" blobs.
    // This wrapper merges those streams into the pipeline and re-emits them as plain text via
    // direct Console writes, which bypass that serialization.
    private const string ScriptTemplate = """
        $ProgressPreference = 'SilentlyContinue'
        $hadError = $false
        & {
        __COMMAND__
        } 2>&1 3>&1 4>&1 5>&1 6>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                [Console]::Error.WriteLine($_.ToString())
                if ($_.FullyQualifiedErrorId -ne 'NativeCommandError') { $hadError = $true }
            } elseif ($_ -is [System.Management.Automation.WarningRecord]) {
                [Console]::Error.WriteLine("WARNING: " + $_.Message)
            } elseif ($_ -is [System.Management.Automation.VerboseRecord] -or $_ -is [System.Management.Automation.DebugRecord]) {
                [Console]::Error.WriteLine($_.Message)
            } elseif ($_ -is [System.Management.Automation.InformationRecord]) {
                [Console]::Out.WriteLine($_.MessageData)
            } else {
                Write-Output $_
            }
        }
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
        if ($hadError) { exit 1 }
        exit 0
        """;

    protected override (string FileName, string Arguments) BuildShellInvocation(string command)
    {
        var script = ScriptTemplate.Replace("__COMMAND__", command);
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return ("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encodedCommand}");
    }
}
