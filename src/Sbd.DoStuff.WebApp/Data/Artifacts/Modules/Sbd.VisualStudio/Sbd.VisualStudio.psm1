# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

function Get-VsWhere {
    # Returns the path to vswhere.exe (installed with the Visual Studio Installer), or $null.
    $programFiles = ${env:ProgramFiles(x86)}
    if (-not $programFiles) { $programFiles = $env:ProgramFiles }
    $candidate = Join-Path $programFiles 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $candidate) { return $candidate }

    $command = Get-Command vswhere -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
}

function Get-VisualStudioVersions {
    <#
    .SYNOPSIS
    Lists every installed version of Visual Studio.
    .DESCRIPTION
    Uses vswhere to find all Visual Studio instances, including previews and instances without a
    complete workload. Writes one status line per instance. Sets $LASTEXITCODE to 0 if an instance at
    least MinVersion is installed (17.0 = Visual Studio 2022), or 1 if none is.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '17.0'
    )

    $vswhere = Get-VsWhere
    if (-not $vswhere) {
        Write-Output "!Visual Studio is not installed (vswhere.exe was not found)."
        $global:LASTEXITCODE = 1
        return
    }

    $json = (& $vswhere -all -prerelease -products * -format json | Out-String).Trim()
    $instances = @()
    if ($json) { $instances = @($json | ConvertFrom-Json) }

    if ($instances.Count -eq 0) {
        Write-Output "!Visual Studio is not installed."
        $global:LASTEXITCODE = 1
        return
    }

    foreach ($instance in ($instances | Sort-Object { [version]$_.installationVersion } -Descending)) {
        $preview = if ($instance.isPrerelease) { ' (preview)' } else { '' }
        Write-Output ":$($instance.displayName) $($instance.catalog.productLineVersion)$preview - $($instance.installationVersion) - $($instance.installationPath)"
    }

    $newest = [version]($instances | Sort-Object { [version]$_.installationVersion } -Descending | Select-Object -First 1).installationVersion
    if ($newest -lt $MinVersion) {
        Write-Output "!Visual Studio version validation failed. Newest installed is $newest, but $MinVersion or later (17.0 = Visual Studio 2022) is required."
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $newest >= $MinVersion"
    $global:LASTEXITCODE = 0
}

function Set-VisualStudioRunAsAdmin {
    <#
    .SYNOPSIS
    Configures every installed Visual Studio to always start as administrator.
    .DESCRIPTION
    Sets the RUNASADMIN compatibility flag on each instance's devenv.exe under
    HKCU\...\AppCompatFlags\Layers (the same thing as ticking "Run this program as an administrator"
    in the file's Compatibility properties). Per-user, so it needs no elevation. Other existing
    compatibility flags on the file are kept. Sets $LASTEXITCODE to 0 on success, or 1 if Visual
    Studio isn't installed.
    #>
    [CmdletBinding()]
    param()

    $vswhere = Get-VsWhere
    if (-not $vswhere) {
        Write-Output "!Visual Studio is not installed (vswhere.exe was not found)."
        $global:LASTEXITCODE = 1
        return
    }

    $json = (& $vswhere -all -prerelease -products * -format json | Out-String).Trim()
    $instances = @()
    if ($json) { $instances = @($json | ConvertFrom-Json) }

    $layers = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers'
    $configured = 0
    foreach ($instance in $instances) {
        $devenv = Join-Path $instance.installationPath 'Common7\IDE\devenv.exe'
        if (-not (Test-Path -LiteralPath $devenv)) { continue }

        if (-not (Test-Path $layers)) { New-Item -Path $layers -Force | Out-Null }
        $current = (Get-ItemProperty -Path $layers -Name $devenv -ErrorAction SilentlyContinue).$devenv
        $flags = @(([string]$current) -split '\s+' | Where-Object { $_ -and $_ -ne '~' -and $_ -ne 'RUNASADMIN' })
        $value = (@('~') + $flags + 'RUNASADMIN') -join ' '

        Set-ItemProperty -Path $layers -Name $devenv -Value $value -Type String
        Write-Output ":$($instance.displayName) will always run as administrator ($devenv)"
        $configured++
    }

    if ($configured -eq 0) {
        Write-Output "!No Visual Studio installation with a devenv.exe was found."
        $global:LASTEXITCODE = 1
        return
    }
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Get-VisualStudioVersions, Set-VisualStudioRunAsAdmin
