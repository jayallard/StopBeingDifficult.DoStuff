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

Export-ModuleMember -Function Get-VisualStudioVersions
