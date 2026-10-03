# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).
#
# IIS is a Windows optional feature, not a winget package, so it's installed with dism.exe, which
# works the same on client and Server SKUs and from both Windows PowerShell 5.1 and PowerShell 7+.
# IIS itself is serviced by Windows Update; "updating" here means enabling any missing features.

$DismRebootRequired = 3010
$OptionalFeatureEnabled = 1

# A typical web server: static content, default documents, errors, logging, request filtering,
# compression, and the IIS Manager console. Parent features are enabled automatically (/All).
$DefaultIisFeatures = @(
    'IIS-WebServerRole'
    'IIS-WebServer'
    'IIS-CommonHttpFeatures'
    'IIS-StaticContent'
    'IIS-DefaultDocument'
    'IIS-HttpErrors'
    'IIS-HealthAndDiagnostics'
    'IIS-HttpLogging'
    'IIS-Security'
    'IIS-RequestFiltering'
    'IIS-Performance'
    'IIS-HttpCompressionStatic'
    'IIS-WebServerManagementTools'
    'IIS-ManagementConsole'
)

function Get-InstalledIisVersion {
    # IIS version from the InetStp registry key (e.g. 10.0), or $null if IIS isn't installed.
    $inetStp = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\InetStp' -ErrorAction SilentlyContinue
    if (-not $inetStp -or $null -eq $inetStp.MajorVersion) {
        return $null
    }

    return [version]"$($inetStp.MajorVersion).$($inetStp.MinorVersion)"
}

function Get-IisBuildVersion {
    # File version of the IIS worker process, which reflects the installed servicing level.
    $w3wp = Join-Path $env:SystemRoot 'System32\inetsrv\w3wp.exe'
    if (-not (Test-Path $w3wp)) {
        return $null
    }

    return (Get-Item $w3wp).VersionInfo.ProductVersion
}

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Install-Iis {
    <#
    .SYNOPSIS
    Installs IIS, or enables any of the requested IIS features that are missing if it's already installed.
    .DESCRIPTION
    Requires an elevated session when anything needs enabling. Sets $LASTEXITCODE to dism's exit code,
    or 0 if every requested feature is already enabled or was enabled (a pending reboot is reported but
    still counts as success).
    #>
    [CmdletBinding()]
    param(
        [string[]]$Feature = $DefaultIisFeatures
    )

    $Feature = @($Feature | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

    $before = Get-InstalledIisVersion
    if ($before) {
        Write-Output ":IIS $before is installed (build $(Get-IisBuildVersion)); checking features"
    }
    else {
        Write-Output ":IIS is not installed; installing"
    }

    $enabled = @(Get-CimInstance Win32_OptionalFeature -Filter "InstallState = $OptionalFeatureEnabled" |
        Where-Object { $_.Name -like 'IIS-*' } |
        ForEach-Object { $_.Name })
    $missing = @($Feature | Where-Object { $enabled -notcontains $_ })

    if ($missing.Count -eq 0) {
        Write-Output ":All requested IIS features are already enabled"
        Write-Output ":IIS updates are delivered through Windows Update"
        $global:LASTEXITCODE = 0
        return
    }

    if (-not (Test-IsElevated)) {
        Write-Output "!Enabling IIS features requires running as Administrator. Missing: $($missing -join ', ')"
        $global:LASTEXITCODE = 5
        return
    }

    Write-Output ":Enabling: $($missing -join ', ')"
    $dismArgs = @('/Online', '/Enable-Feature', '/All', '/NoRestart', '/Quiet') +
        @($missing | ForEach-Object { "/FeatureName:$_" })
    dism.exe @dismArgs

    if ($LASTEXITCODE -eq $DismRebootRequired) {
        Write-Output ":A restart is required to finish enabling IIS features"
        $global:LASTEXITCODE = 0
    }
    elseif ($LASTEXITCODE -ne 0) {
        Write-Output "!dism failed with exit code $LASTEXITCODE"
        return
    }

    $after = Get-InstalledIisVersion
    if (-not $after) {
        Write-Output "!dism succeeded but IIS still isn't reported as installed"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":IIS $after is installed (build $(Get-IisBuildVersion))"
    $global:LASTEXITCODE = 0
}

function Uninstall-Iis {
    <#
    .SYNOPSIS
    Completely uninstalls IIS along with every IIS feature (module) enabled on the machine.
    .DESCRIPTION
    Disables every enabled optional feature named IIS-* (including any added after the initial install,
    such as ASP.NET, CGI or URL Rewrite-style add-ons shipped as Windows features) plus the Windows
    Process Activation Service (WAS-*) that IIS depends on. Content under inetpub is left untouched.
    Requires an elevated session. Sets $LASTEXITCODE to 0 on success or if IIS isn't installed (a pending
    reboot is reported but still counts as success).
    #>
    [CmdletBinding()]
    param()

    $enabled = @(Get-CimInstance Win32_OptionalFeature -Filter "InstallState = $OptionalFeatureEnabled" |
        Where-Object { $_.Name -like 'IIS-*' -or $_.Name -like 'WAS-*' } |
        ForEach-Object { $_.Name } |
        Sort-Object)

    if ($enabled.Count -eq 0) {
        Write-Output ":IIS is not installed; nothing to remove"
        $global:LASTEXITCODE = 0
        return
    }

    if (-not (Test-IsElevated)) {
        Write-Output "!Removing IIS requires running as Administrator. Enabled: $($enabled -join ', ')"
        $global:LASTEXITCODE = 5
        return
    }

    Write-Output ":Disabling: $($enabled -join ', ')"
    $dismArgs = @('/Online', '/Disable-Feature', '/NoRestart', '/Quiet') +
        @($enabled | ForEach-Object { "/FeatureName:$_" })
    dism.exe @dismArgs

    if ($LASTEXITCODE -eq $DismRebootRequired) {
        Write-Output ":A restart is required to finish removing IIS"
        $global:LASTEXITCODE = 0
    }
    elseif ($LASTEXITCODE -ne 0) {
        Write-Output "!dism failed with exit code $LASTEXITCODE"
        return
    }

    Write-Output ":IIS has been removed ($($enabled.Count) features disabled)"
    $global:LASTEXITCODE = 0
}

function Test-IisVersion {
    <#
    .SYNOPSIS
    Checks that IIS is installed and is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if IIS is older than MinVersion, or 2 if IIS isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '10.0'
    )

    $version = Get-InstalledIisVersion
    if (-not $version) {
        Write-Output "!IIS is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    Write-Output ":IIS Version: $version (build $(Get-IisBuildVersion))"
    if ($version -lt $MinVersion) {
        Write-Output "!Failed: $version < MinVersion $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Install-Iis, Uninstall-Iis, Test-IisVersion
