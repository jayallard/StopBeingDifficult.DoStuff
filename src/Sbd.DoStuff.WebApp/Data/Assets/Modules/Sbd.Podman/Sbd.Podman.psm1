# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

$WinGetNoApplicableUpgrade = -1978335189
$WinGetNoPackageFound = -1978335212

function Update-SessionPath {
    # winget updates the persisted PATH, but this process keeps the PATH it started with, so a
    # freshly installed podman wouldn't be found until the app restarts. Re-read it from the registry.
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user, $env:Path) | Where-Object { $_ }) -join ';'
}

function Get-Podman {
    # Returns the path to podman, or $null if it isn't installed.
    $command = Get-Command podman -ErrorAction SilentlyContinue
    if (-not $command) { Update-SessionPath; $command = Get-Command podman -ErrorAction SilentlyContinue }
    if ($command) { return $command.Source }

    $candidate = Join-Path $env:ProgramFiles 'RedHat\Podman\podman.exe'
    if (Test-Path $candidate) { return $candidate }
}

function Remove-OrphanedPodmanDistro {
    # A failed `podman machine init` can leave its WSL distro registered even though `podman machine list`
    # shows nothing; the next init then fails on the name clash, or on the half-built guest. Only called
    # when podman itself reports no machines, so the distro is never one a podman machine owns.
    $previous = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [System.Text.Encoding]::Unicode   # wsl.exe writes UTF-16
        $distros = @(wsl --list --quiet) | ForEach-Object { "$_".Trim() }
    }
    finally { [Console]::OutputEncoding = $previous }

    if ($distros -contains 'podman-machine-default') {
        Write-Output ":Removing the leftover WSL distro from a previous failed machine init"
        wsl --unregister podman-machine-default | Out-Null
    }
}

function Install-Podman {
    <#
    .SYNOPSIS
    Installs Podman via winget and creates and starts a default Podman machine.
    .DESCRIPTION
    Installs Podman if it isn't already installed, then initializes and starts the default
    Podman machine if none exists yet. Sets $LASTEXITCODE to 0 on success.
    .PARAMETER Version
    The Podman version to install. Defaults to 6.1.4.
    #>
    [CmdletBinding()]
    param(
        [string]$Version = '6.1.4'
    )

    winget install --id RedHat.Podman -e --source winget --version $Version
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":Podman is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
    if ($LASTEXITCODE -ne 0) { return }

    Update-SessionPath
    $podman = Get-Podman
    if (-not $podman) {
        Write-Output "!podman was not found after installing Podman."
        $global:LASTEXITCODE = 1
        return
    }

    $machines = & $podman machine list --format '{{.Name}}' 2>$null
    if ($machines) {
        Write-Output ":A Podman machine already exists"
        $running = & $podman machine list --format '{{.Running}}' 2>$null
        if ($running -notcontains 'true') {
            & $podman machine start
            if ($LASTEXITCODE -ne 0) {
                Write-Output "!podman machine start failed with exit code $LASTEXITCODE."
                return
            }
        }
    }
    else {
        Remove-OrphanedPodmanDistro

        # The first boot of the new guest can fail transiently (e.g. exit status 0xc000070a while
        # restoring package permissions), so give it a second try from a clean WSL state.
        foreach ($attempt in 1..2) {
            Write-Output ":Creating the default Podman machine (attempt $attempt of 2)"
            & $podman machine init
            if ($LASTEXITCODE -eq 0) { break }

            if ($attempt -eq 2) {
                Write-Output "!podman machine init failed with exit code $LASTEXITCODE. If WSL reports 0xc000070a, WSL itself is in a bad state: run 'wsl --shutdown' (this stops all WSL distros) and run this task again."
                return
            }

            Write-Output ":podman machine init failed with exit code $LASTEXITCODE; retrying"
            Remove-OrphanedPodmanDistro
        }

        & $podman machine start
        if ($LASTEXITCODE -ne 0) {
            Write-Output "!podman machine start failed with exit code $LASTEXITCODE."
            return
        }
    }

    Write-Output ":Podman is installed: $(& $podman --version)"
    $global:LASTEXITCODE = 0
}

function Uninstall-Podman {
    <#
    .SYNOPSIS
    Removes every Podman machine and uninstalls Podman via winget.
    .DESCRIPTION
    Stops and deletes all Podman machines (and with them every container and image stored in them), removes
    a leftover podman-machine-default WSL distro if one is still registered, then uninstalls Podman. Succeeds
    if Podman isn't installed. Sets $LASTEXITCODE to 0 on success.
    #>
    [CmdletBinding()]
    param()

    $podman = Get-Podman
    if ($podman) {
        foreach ($machine in @(& $podman machine list --format '{{.Name}}' 2>$null)) {
            $name = "$machine".Trim() -replace '\*$', ''   # the default machine is listed with a trailing *
            if (-not $name) { continue }

            Write-Output ":Removing Podman machine $name"
            & $podman machine rm -f $name
            if ($LASTEXITCODE -ne 0) {
                Write-Output "!podman machine rm failed for $name with exit code $LASTEXITCODE."
                return
            }
        }

        Remove-OrphanedPodmanDistro
    }

    winget uninstall --id RedHat.Podman -e --source winget
    if ($LASTEXITCODE -eq $WinGetNoPackageFound) {
        Write-Output ":Podman is not installed"
        $global:LASTEXITCODE = 0
    }
}

function Install-PodmanCompose {
    <#
    .SYNOPSIS
    Adds the compose component to Podman.
    .DESCRIPTION
    `podman compose` is a thin wrapper that delegates to an external compose provider. This installs
    the Docker Compose standalone binary via winget (the provider Podman prefers) and verifies that
    `podman compose version` works. Requires Podman to be installed. Sets $LASTEXITCODE to 0 on success.
    #>
    [CmdletBinding()]
    param()

    $podman = Get-Podman
    if (-not $podman) {
        Write-Output "!Podman is not installed. Run the Install Podman task first."
        $global:LASTEXITCODE = 1
        return
    }

    winget install --id Docker.DockerCompose -e --source winget
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":Docker Compose is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
    if ($LASTEXITCODE -ne 0) { return }

    Update-SessionPath
    Repair-PodmanDockerApi
    if ($LASTEXITCODE -ne 0) { return }

    $env:PODMAN_COMPOSE_WARNING_LOGS = 'false'
    $output = & $podman compose version 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Output "!podman compose version failed with exit code ${LASTEXITCODE}: $($output.Trim())"
        return
    }

    Write-Output ":Podman compose is ready: $($output.Trim())"
    $global:LASTEXITCODE = 0
}

function Repair-PodmanDockerApi {
    <#
    .SYNOPSIS
    Makes sure the Docker API named pipe that compose providers connect to exists.
    .DESCRIPTION
    `podman compose` delegates to docker-compose, which reaches the machine through the
    \\.\pipe\podman-machine-default named pipe served by win-sshproxy. After an unclean shutdown a stale
    podman-machine-default-api.sock in %TEMP%\podman makes win-sshproxy fail to start, so the pipe is
    missing ("failed to connect to the docker API"). If the pipe is missing, this deletes that stale socket
    and restarts the machine (running containers are stopped; those with a restart policy come back).
    Sets $LASTEXITCODE to 0 if the pipe exists afterwards.
    #>
    [CmdletBinding()]
    param()

    $podman = Get-Podman
    if (-not $podman) {
        Write-Output "!Podman is not installed. Run the Install Podman task first."
        $global:LASTEXITCODE = 1
        return
    }

    $pipeName = 'podman-machine-default'
    $pipePath = '\\.\pipe\' + $pipeName
    if ([IO.Directory]::GetFiles('\\.\pipe\') -contains $pipePath) {
        $global:LASTEXITCODE = 0
        return
    }

    Write-Output ":The Docker API pipe is missing; restarting the Podman machine to recreate it"
    & $podman machine stop 2>&1 | Out-Null
    Remove-Item -LiteralPath (Join-Path $env:TEMP "podman\$pipeName-api.sock") -Force -ErrorAction SilentlyContinue
    & $podman machine start 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Output "!podman machine start failed with exit code $LASTEXITCODE."
        return
    }

    if ([IO.Directory]::GetFiles('\\.\pipe\') -notcontains $pipePath) {
        Write-Output "!The Docker API pipe is still missing after restarting the Podman machine. Run 'podman machine start' to see why win-sshproxy fails."
        $global:LASTEXITCODE = 1
        return
    }

    $global:LASTEXITCODE = 0
}

function Start-PodmanCompose {
    <#
    .SYNOPSIS
    Starts the services in a compose file in the background (`podman compose up -d`).
    .PARAMETER ComposeFile
    Full path to the compose file.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ComposeFile
    )

    if (-not (Test-Path -LiteralPath $ComposeFile)) {
        Write-Output "!Compose file not found: $ComposeFile"
        $global:LASTEXITCODE = 1
        return
    }

    $podman = Get-Podman
    if (-not $podman) {
        Write-Output "!Podman is not installed. Run the Install Podman task first."
        $global:LASTEXITCODE = 1
        return
    }

    Update-SessionPath   # docker-compose is installed by winget after the app may have started
    Repair-PodmanDockerApi
    if ($LASTEXITCODE -ne 0) { return }

    $env:PODMAN_COMPOSE_WARNING_LOGS = 'false'
    Write-Output ":Starting services from $ComposeFile"
    & $podman compose -f $ComposeFile up -d
    if ($LASTEXITCODE -ne 0) {
        Write-Output "!podman compose up failed with exit code $LASTEXITCODE."
    }
}

function Add-PodmanInsecureRegistry {
    <#
    .SYNOPSIS
    Allows plain-HTTP (and untrusted-TLS) access to a container registry from Podman.
    .DESCRIPTION
    Podman refuses http registries by default. Pulls run inside the Podman machine, so this writes a
    registries.conf.d drop-in there marking the registry insecure and restarts the machine's podman service
    so it takes effect. Does nothing if the registry is
    already marked insecure. The registry URL may include a scheme and path (http://host:5000/); only
    host[:port] is used. Sets $LASTEXITCODE to 0 on success.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RegistryUrl
    )

    $podman = Get-Podman
    if (-not $podman) {
        Write-Output "!Podman is not installed. Run the Install Podman task first."
        $global:LASTEXITCODE = 1
        return
    }

    $registry = ($RegistryUrl.Trim() -replace '^[a-zA-Z][a-zA-Z0-9+.-]*://', '' -replace '/.*$', '')
    if ($registry -notmatch '^[A-Za-z0-9.-]+(:\d+)?$') {
        Write-Output "!'$RegistryUrl' is not a valid registry address (expected host or host:port)."
        $global:LASTEXITCODE = 1
        return
    }

    $escaped = [regex]::Escape($registry)
    $check = "grep -rhA3 -E '^[[:space:]]*location[[:space:]]*=[[:space:]]*`"$escaped`"' /etc/containers/registries.conf /etc/containers/registries.conf.d 2>/dev/null | grep -qE 'insecure[[:space:]]*=[[:space:]]*true'"
    & $podman machine ssh $check
    if ($LASTEXITCODE -eq 0) {
        Write-Output ":$registry is already allowed as an insecure registry"
        $global:LASTEXITCODE = 0
        return
    }

    $fileName = 'insecure-' + ($registry -replace '[^A-Za-z0-9.-]', '_') + '.conf'
    $content = "[[registry]]`nlocation = `"$registry`"`ninsecure = true`n"
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($content))

    Write-Output ":Allowing insecure access to $registry"
    & $podman machine ssh "sudo mkdir -p /etc/containers/registries.conf.d && echo $encoded | base64 -d | sudo tee /etc/containers/registries.conf.d/$fileName > /dev/null"
    if ($LASTEXITCODE -ne 0) {
        Write-Output "!Failed to write the registry configuration in the Podman machine (exit code $LASTEXITCODE). Is the machine running?"
        return
    }

    # The machine's podman service caches registries.conf, so restart it (the socket re-activates it on
    # demand); running containers are unaffected.
    & $podman machine ssh 'sudo systemctl restart podman.socket; sudo systemctl stop podman.service' 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Output "!The registry was configured, but restarting the Podman service failed (exit code $LASTEXITCODE). Restart the Podman machine to apply it."
        return
    }

    Write-Output ":$registry is now allowed as an insecure registry"
    $global:LASTEXITCODE = 0
}

function Test-PodmanVersion {
    <#
    .SYNOPSIS
    Checks that the installed Podman is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if Podman is older than MinVersion, or 2 if it isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '1.0.0'
    )

    $podman = Get-Podman
    if (-not $podman) {
        Write-Output "!Podman is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    # e.g. "podman version 5.8.3"
    $versionText = (& $podman --version 2>&1 | Out-String).Trim()
    if ($versionText -notmatch '\b(\d+\.\d+\.\d+)\b') {
        Write-Output "!Could not parse the Podman version from '$versionText'."
        $global:LASTEXITCODE = 1
        return
    }

    $version = [version]$Matches[1]
    Write-Output ":Podman Version: $version"
    if ($version -lt $MinVersion) {
        Write-Output "!Podman Version validation failed. $version < $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Add-PodmanInsecureRegistry, Test-PodmanVersion, Install-Podman, Uninstall-Podman, Install-PodmanCompose, Repair-PodmanDockerApi, Start-PodmanCompose
