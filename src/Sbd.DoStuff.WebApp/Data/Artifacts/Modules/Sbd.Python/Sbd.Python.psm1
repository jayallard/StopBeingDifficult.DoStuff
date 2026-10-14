# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

$WinGetNoApplicableUpgrade = -1978335189

function Update-SessionPath {
    # winget updates the persisted PATH, but this process keeps the PATH it started with, so a
    # freshly installed python wouldn't be found until the app restarts. Re-read it from the registry.
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user, $env:Path) | Where-Object { $_ }) -join ';'
}

function Get-PythonVersion {
    # Returns the installed Python version, or $null. Ignores the Windows Store "python" stub,
    # which is on PATH but prints no version.
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) { Update-SessionPath }
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) { return $null }
    $text = "$(python --version 2>&1)"
    if ($text -match '\bPython (\d+\.\d+(?:\.\d+)?)') { return [version]$Matches[1] }
    return $null
}

function Install-Python {
    <#
    .SYNOPSIS
    Installs Python via winget, if it isn't already installed.
    .DESCRIPTION
    Version is the major.minor series to install (e.g. 3.13). Sets $LASTEXITCODE to 0 if Python is
    already installed or was installed successfully, otherwise to winget's exit code.
    #>
    [CmdletBinding()]
    param(
        [string]$Version = '3.13'
    )

    $existing = Get-PythonVersion
    if ($existing) {
        Write-Output ":Python $existing is already installed"
        $global:LASTEXITCODE = 0
        return
    }

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Output '!WinGet is not installed.'
        $global:LASTEXITCODE = 2
        return
    }

    winget install --id "Python.Python.$Version" -e --source winget
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":Python is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
    if ($LASTEXITCODE -eq 0) { Update-SessionPath }
}

function Test-PythonVersion {
    <#
    .SYNOPSIS
    Checks that the installed Python is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if Python is older than MinVersion, or 2 if Python isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '3.10'
    )

    $version = Get-PythonVersion
    if (-not $version) {
        Write-Output '!Python is not installed.'
        $global:LASTEXITCODE = 2
        return
    }

    Write-Output ":Python Version: $version"
    if ($version -lt $MinVersion) {
        Write-Output "!Failed: $version < MinVersion $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Install-Python, Test-PythonVersion
