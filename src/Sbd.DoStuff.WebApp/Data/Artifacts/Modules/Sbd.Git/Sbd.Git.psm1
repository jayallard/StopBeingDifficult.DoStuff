# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

$WinGetNoApplicableUpgrade = -1978335189

function Install-Git {
    <#
    .SYNOPSIS
    Installs Git via winget, if it isn't already installed.
    .DESCRIPTION
    Sets $LASTEXITCODE to winget's exit code, or 0 if Git is already installed and up to date.
    #>
    [CmdletBinding()]
    param()

    winget install --id Git.Git -e --source winget
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":Git is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
}

function Test-GitVersion {
    <#
    .SYNOPSIS
    Checks that the installed Git CLI is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if Git is older than MinVersion, or 2 if Git isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '2.53.0'
    )

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Output "!Git is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    $gitVersion = git --version
    if ($gitVersion -notmatch '\b(\d+\.\d+\.\d+)\b') {
        Write-Output "!Could not parse the Git version from '$gitVersion'."
        $global:LASTEXITCODE = 1
        return
    }

    $version = [version]$Matches[1]
    Write-Output ":Git Version: $version"
    if ($version -lt $MinVersion) {
        Write-Output "!Git Version validation failed. $version < $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Install-Git, Test-GitVersion
