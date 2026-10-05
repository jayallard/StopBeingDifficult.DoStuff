# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

$WinGetNoApplicableUpgrade = -1978335189

function Update-SessionPath {
    # winget updates the persisted PATH, but this process keeps the PATH it started with, so a
    # freshly installed fnm wouldn't be found until the app restarts. Re-read it from the registry.
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user, $env:Path) | Where-Object { $_ }) -join ';'
}

function Install-Fnm {
    <#
    .SYNOPSIS
    Installs fnm (Fast Node Manager) via winget, if it isn't already installed.
    .DESCRIPTION
    Sets $LASTEXITCODE to winget's exit code, or 0 if fnm is already installed and up to date.
    #>
    [CmdletBinding()]
    param()

    winget install --id Schniz.fnm -e --source winget
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":fnm is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
    if ($LASTEXITCODE -eq 0) { Update-SessionPath }
}

function Test-FnmVersion {
    <#
    .SYNOPSIS
    Checks that the installed fnm is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if fnm is older than MinVersion, or 2 if fnm isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '1.38.1'
    )

    if (-not (Get-Command fnm -ErrorAction SilentlyContinue)) { Update-SessionPath }
    if (-not (Get-Command fnm -ErrorAction SilentlyContinue)) {
        Write-Output "!fnm is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    $fnmVersion = fnm --version
    if ("$fnmVersion" -notmatch '\b(\d+\.\d+\.\d+)\b') {
        Write-Output "!Could not parse the fnm version from '$fnmVersion'."
        $global:LASTEXITCODE = 1
        return
    }

    $version = [version]$Matches[1]
    Write-Output ":fnm Version: $version"
    if ($version -lt $MinVersion) {
        Write-Output "!fnm Version validation failed. $version < $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Install-Fnm, Test-FnmVersion
