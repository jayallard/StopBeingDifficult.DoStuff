# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

$WinGetNoApplicableUpgrade = -1978335189
$WinGetNoPackageFound = -1978335212

function Update-SessionPath {
    # winget updates the persisted PATH, but this process keeps the PATH it started with, so a
    # freshly installed rdctl wouldn't be found until the app restarts. Re-read it from the registry.
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user, $env:Path) | Where-Object { $_ }) -join ';'
}

function Get-RdCtl {
    # Returns the path to rdctl, or $null if Rancher Desktop isn't installed.
    $command = Get-Command rdctl -ErrorAction SilentlyContinue
    if (-not $command) { Update-SessionPath; $command = Get-Command rdctl -ErrorAction SilentlyContinue }
    if ($command) { return $command.Source }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Rancher Desktop\resources\resources\win32\bin\rdctl.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Rancher Desktop\resources\resources\win32\bin\rdctl.exe')
    )
    $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

function Install-Rancher {
    <#
    .SYNOPSIS
    Installs Rancher Desktop via winget, using the moby container engine and starting with Windows.
    .DESCRIPTION
    Installs Rancher Desktop if it isn't already installed, then starts it with the moby (dockerd)
    container engine and automatic start at login. Sets $LASTEXITCODE to 0 on success.
    #>
    [CmdletBinding()]
    param()

    winget install --id SUSE.RancherDesktop -e --source winget
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":Rancher Desktop is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
    if ($LASTEXITCODE -ne 0) { return }

    Update-SessionPath
    $rdctl = Get-RdCtl
    if (-not $rdctl) {
        Write-Output "!rdctl was not found after installing Rancher Desktop."
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Configuring Rancher Desktop: moby container engine, start automatically with Windows"
    & $rdctl start --container-engine.name=moby --application.auto-start=true
    if ($LASTEXITCODE -ne 0) {
        Write-Output "!rdctl start failed with exit code $LASTEXITCODE."
        return
    }

    Write-Output ":Rancher Desktop is starting; it may take a few minutes before the container engine is ready"
    $global:LASTEXITCODE = 0
}

function Uninstall-Rancher {
    <#
    .SYNOPSIS
    Uninstalls Rancher Desktop via winget.
    .DESCRIPTION
    Shuts down Rancher Desktop if it is running, then uninstalls it. Succeeds if it isn't installed.
    Sets $LASTEXITCODE to 0 on success.
    #>
    [CmdletBinding()]
    param()

    $rdctl = Get-RdCtl
    if ($rdctl) {
        Write-Output ":Shutting down Rancher Desktop"
        & $rdctl shutdown
    }

    winget uninstall --id SUSE.RancherDesktop -e --source winget
    if ($LASTEXITCODE -eq $WinGetNoPackageFound) {
        Write-Output ":Rancher Desktop is not installed"
        $global:LASTEXITCODE = 0
    }
}

function Test-RancherVersion {
    <#
    .SYNOPSIS
    Checks that the installed Rancher Desktop is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if Rancher Desktop is older than MinVersion, or 2 if it isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '1.0.0'
    )

    $rdctl = Get-RdCtl
    if (-not $rdctl) {
        Write-Output "!Rancher Desktop is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    # e.g. "rdctl client version: 1.19.2, targeting server version: v1beta"
    $versionText = (& $rdctl version 2>&1 | Out-String).Trim()
    if ($versionText -notmatch '(?i)client version:\s*v?(\d+\.\d+\.\d+)' -and $versionText -notmatch '\b(\d+\.\d+\.\d+)\b') {
        Write-Output "!Could not parse the Rancher Desktop version from '$versionText'."
        $global:LASTEXITCODE = 1
        return
    }

    $version = [version]$Matches[1]
    Write-Output ":Rancher Desktop Version: $version"
    if ($version -lt $MinVersion) {
        Write-Output "!Rancher Desktop Version validation failed. $version < $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Install-Rancher, Uninstall-Rancher, Test-RancherVersion
