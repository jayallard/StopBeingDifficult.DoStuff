# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

function Test-VsCodeInstalled {
    <#
    .SYNOPSIS
    Checks that VS Code's `code` CLI is on PATH.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if VS Code is installed, otherwise 1.
    #>
    [CmdletBinding()]
    param()

    if (Get-Command code -ErrorAction SilentlyContinue) {
        Write-Output ":VS Code is installed."
        $global:LASTEXITCODE = 0
        return
    }

    Write-Output "!VS Code is NOT installed."
    $global:LASTEXITCODE = 1
}

Export-ModuleMember -Function Test-VsCodeInstalled
