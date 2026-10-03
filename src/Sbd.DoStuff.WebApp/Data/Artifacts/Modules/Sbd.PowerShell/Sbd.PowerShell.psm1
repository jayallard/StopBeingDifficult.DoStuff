# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).
#
# Everything here works from both Windows PowerShell 5.1 and PowerShell 7+, so the tasks can run
# under whichever one the app picks (pwsh.exe if it exists, otherwise powershell.exe).

$WinGetNoApplicableUpgrade = -1978335189
$PowerShellPackageId = 'Microsoft.PowerShell'

function Get-InstalledPowerShellVersion {
    # Version of the pwsh.exe on PATH (not the session running this code, which may be 5.1),
    # or $null if PowerShell 7+ isn't installed.
    if (-not (Get-Command pwsh -ErrorAction SilentlyContinue)) {
        return $null
    }

    $versionText = pwsh -NoLogo -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()'
    $match = [regex]::Match("$versionText", '\d+(?:\.\d+){1,3}')
    if (-not $match.Success) {
        return $null
    }

    return [version]$match.Value
}

function Install-PowerShell {
    <#
    .SYNOPSIS
    Installs PowerShell 7+ via winget, or updates it if it's already installed.
    .DESCRIPTION
    Sets $LASTEXITCODE to winget's exit code, or 0 if PowerShell is already installed and up to date.
    #>
    [CmdletBinding()]
    param()

    Write-Output ":Running under PowerShell $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"

    $before = Get-InstalledPowerShellVersion
    if ($before) {
        Write-Output ":PowerShell $before is installed; checking for updates"
        winget upgrade --id $PowerShellPackageId -e --source winget
    }
    else {
        Write-Output ":PowerShell 7+ is not installed; installing"
        winget install --id $PowerShellPackageId -e --source winget
    }

    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":PowerShell is already installed and is up to date"
        $global:LASTEXITCODE = 0
        return
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Output "!winget failed with exit code $LASTEXITCODE"
        return
    }

    # A fresh install isn't on this process's PATH yet, so look in the default install folder too.
    $after = Get-InstalledPowerShellVersion
    if (-not $after) {
        $defaultPwsh = Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'
        if (Test-Path $defaultPwsh) {
            $after = [version](& $defaultPwsh -NoLogo -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()')
        }
    }
    Write-Output ":PowerShell $after is installed"
    $global:LASTEXITCODE = 0
}

function Test-PowerShellVersion {
    <#
    .SYNOPSIS
    Checks that the installed PowerShell 7+ (pwsh) is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if pwsh is older than MinVersion, or 2 if pwsh isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '7.5.0'
    )

    $version = Get-InstalledPowerShellVersion
    if (-not $version) {
        Write-Output "!PowerShell 7+ is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    Write-Output ":PowerShell Version: $version"
    if ($version -lt $MinVersion) {
        Write-Output "!Failed: $version < MinVersion $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Install-PowerShell, Test-PowerShellVersion
