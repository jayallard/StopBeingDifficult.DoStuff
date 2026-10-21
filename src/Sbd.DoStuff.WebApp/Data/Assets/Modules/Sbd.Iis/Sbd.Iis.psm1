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

$IisConfigPath = Join-Path $env:SystemRoot 'System32\inetsrv\config\applicationHost.config'

# Native modules a working IIS must have registered. When applicationHost.config survives an IIS
# uninstall it can lose these, and every request then gets a bare 401 (no module authenticates it).
$IisCoreModules = 'AnonymousAuthenticationModule', 'StaticFileModule', 'DefaultDocumentModule'

function Test-IisModuleRegistered {
    # $true/$false whether applicationHost.config registers the native module; $null if it can't be
    # read (not elevated), in which case callers shouldn't draw conclusions.
    param([Parameter(Mandatory)][string]$Name)

    try { $xml = [xml][IO.File]::ReadAllText($IisConfigPath) }
    catch [IO.FileNotFoundException] { return $false }
    catch [IO.DirectoryNotFoundException] { return $false }
    catch { return $null }
    return $null -ne $xml.SelectSingleNode("/configuration/system.webServer/globalModules/add[@name='$Name']")
}

function Test-IisConfigHealthy {
    # $false if applicationHost.config is missing any core native module; $true otherwise (or unreadable).
    foreach ($name in $IisCoreModules) {
        if ((Test-IisModuleRegistered $name) -eq $false) { return $false }
    }
    return $true
}

function Test-RestartPending {
    # True if component servicing (dism/Windows Update) is waiting for a restart.
    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending',
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootInProgress',
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\PackagesPending',
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
    foreach ($key in $keys) {
        if (Test-Path $key) { return $true }
    }
    return $false
}

function Stop-IisServices {
    foreach ($name in 'W3SVC', 'WAS') {
        if (Get-Service $name -ErrorAction SilentlyContinue) {
            Stop-Service $name -Force -ErrorAction SilentlyContinue
        }
    }
}

function Backup-IisConfig {
    # Moves applicationHost.config aside so a fresh one is generated when IIS is next installed.
    # IIS must be removed first; a surviving config is what leaves a reinstall broken.
    Stop-IisServices
    if (-not (Test-Path -LiteralPath $IisConfigPath)) { return }
    $backup = "$IisConfigPath.removed-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    try {
        Move-Item -LiteralPath $IisConfigPath -Destination $backup -ErrorAction Stop
        Write-Output ":Moved applicationHost.config aside to $backup"
    }
    catch {
        Write-Output "!Couldn't move applicationHost.config aside: $($_.Exception.Message)"
    }
}

function Initialize-IisDefaultWebSite {
    # Makes sure a "Default Web Site" exists (http *:80 -> %SystemDrive%\inetpub\wwwroot). Enabling the
    # features doesn't guarantee one: applicationHost.config can survive an earlier uninstall without it.
    # Sets $LASTEXITCODE to 0 on success.
    $siteName = 'Default Web Site'
    $appcmd = Join-Path $env:SystemRoot 'System32\inetsrv\appcmd.exe'
    if (-not (Test-Path $appcmd)) {
        Write-Output "!appcmd.exe not found; can't create the $siteName"
        $global:LASTEXITCODE = 1
        return
    }

    # A surviving applicationHost.config may lack DefaultAppPool, which leaves the site with an
    # "invalid app pool name". Make sure the pool exists, and that the site uses it.
    $poolName = 'DefaultAppPool'
    $null = & $appcmd list apppool "/name:$poolName"
    if ($LASTEXITCODE -ne 0) {
        Write-Output ":Creating app pool $poolName"
        & $appcmd add apppool "/name:$poolName"
        if ($LASTEXITCODE -ne 0) {
            Write-Output "!Failed to create the $poolName app pool (appcmd exit code $LASTEXITCODE)"
            return
        }
    }

    $null = & $appcmd list site "/name:$siteName"
    if ($LASTEXITCODE -eq 0) {
        $currentPool = (& $appcmd list app "$siteName/" /text:applicationPool) | Select-Object -First 1
        $null = & $appcmd list apppool "/name:$currentPool"
        if (-not $currentPool -or $LASTEXITCODE -ne 0) {
            Write-Output ":$siteName uses missing app pool '$currentPool'; switching it to $poolName"
            & $appcmd set app "$siteName/" "/applicationPool:$poolName"
            if ($LASTEXITCODE -ne 0) {
                Write-Output "!Failed to assign $poolName to the $siteName (appcmd exit code $LASTEXITCODE)"
                return
            }
        }
        else {
            Write-Output ":$siteName already exists"
        }
        $global:LASTEXITCODE = 0
        return
    }

    $physicalPath = Join-Path $env:SystemDrive 'inetpub\wwwroot'
    if (-not (Test-Path $physicalPath)) {
        $null = New-Item -ItemType Directory -Path $physicalPath -Force
    }

    Write-Output ":Creating $siteName (http *:80 -> $physicalPath)"
    & $appcmd add site "/name:$siteName" "/physicalPath:$physicalPath" '/bindings:http/*:80:'
    if ($LASTEXITCODE -ne 0) {
        Write-Output "!Failed to create the $siteName (appcmd exit code $LASTEXITCODE)"
        return
    }

    & $appcmd set app "$siteName/" "/applicationPool:$poolName"
    if ($LASTEXITCODE -ne 0) {
        Write-Output "!Failed to assign $poolName to the $siteName (appcmd exit code $LASTEXITCODE)"
        return
    }

    $global:LASTEXITCODE = 0
}

function Install-Iis {
    <#
    .SYNOPSIS
    Installs IIS, or enables any of the requested IIS features that are missing if it's already installed.
    .DESCRIPTION
    Requires an elevated session when anything needs enabling. Also creates the "Default Web Site"
    (http port 80, inetpub\wwwroot) if it doesn't exist. Sets $LASTEXITCODE to dism's exit code,
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

    if ($before -and $missing.Count -eq 0 -and -not (Test-IisConfigHealthy)) {
        # The features are on, but applicationHost.config doesn't register the core modules (a leftover
        # from an earlier uninstall). Enabling features again won't fix that: remove IIS, set the config
        # aside, and reinstall so IIS generates a fresh one.
        Write-Output ":applicationHost.config is missing core IIS modules; removing IIS so it can be reinstalled cleanly"
        Uninstall-Iis
        if ($LASTEXITCODE -ne 0) { return }
        $enabled = @(Get-CimInstance Win32_OptionalFeature -Filter "InstallState = $OptionalFeatureEnabled" |
            Where-Object { $_.Name -like 'IIS-*' } |
            ForEach-Object { $_.Name })
        $missing = @($Feature | Where-Object { $enabled -notcontains $_ })
    }

    if ($missing.Count -eq 0) {
        Write-Output ":All requested IIS features are already enabled"
        Write-Output ":IIS updates are delivered through Windows Update"
        if (Get-InstalledIisVersion) {
            if (-not (Test-IsElevated)) {
                Write-Output "!Creating the Default Web Site requires running as Administrator"
                $global:LASTEXITCODE = 5
                return
            }
            Initialize-IisDefaultWebSite
            return
        }
        $global:LASTEXITCODE = 0
        return
    }

    if (-not (Test-IsElevated)) {
        Write-Output "!Enabling IIS features requires running as Administrator. Missing: $($missing -join ', ')"
        $global:LASTEXITCODE = 5
        return
    }

    # Enabling features while a removal is still pending a restart fails with dism error 2.
    if (Test-RestartPending) {
        Write-Output "!A restart is required to finish removing IIS. Restart Windows, then run this again."
        Write-Output "::RestartComputer"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Enabling: $($missing -join ', ')"
    $dismArgs = @('/Online', '/Enable-Feature', '/All', '/NoRestart') +
        @($missing | ForEach-Object { "/FeatureName:$_" })
    dism.exe @dismArgs

    if ($LASTEXITCODE -eq $DismRebootRequired) {
        Write-Output ":A restart is required to finish enabling IIS features"
        Write-Output "::RestartComputer"
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
    Initialize-IisDefaultWebSite
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
        if ((Test-IsElevated) -and (Test-Path -LiteralPath $IisConfigPath)) { Backup-IisConfig }
        $global:LASTEXITCODE = 0
        return
    }

    if (-not (Test-IsElevated)) {
        Write-Output "!Removing IIS requires running as Administrator. Enabled: $($enabled -join ', ')"
        $global:LASTEXITCODE = 5
        return
    }

    Write-Output ":Disabling: $($enabled -join ', ')"
    Stop-IisServices
    $dismArgs = @('/Online', '/Disable-Feature', '/NoRestart') +
        @($enabled | ForEach-Object { "/FeatureName:$_" })
    dism.exe @dismArgs

    if ($LASTEXITCODE -eq $DismRebootRequired) {
        Write-Output ":A restart is required to finish removing IIS"
        Write-Output "::RestartComputer"
        $global:LASTEXITCODE = 0
    }
    elseif ($LASTEXITCODE -ne 0) {
        Write-Output "!dism failed with exit code $LASTEXITCODE"
        return
    }

    # Leave no applicationHost.config behind: a reinstall doesn't repair a surviving one.
    Backup-IisConfig

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

# --- IIS modules and hosting bundles -------------------------------------------------------------
# URL Rewrite, Application Request Routing and the ASP.NET Core Hosting Bundles each have a Test-*
# (exit 0 = installed, 2 = not installed), Install-* and Uninstall-* function. Installs and
# uninstalls are idempotent and need an elevated session. Install the hosting bundles *after* IIS
# itself so the ASP.NET Core Module gets registered with it.

$WinGetNoApplicableUpgrade = -1978335189
$WinGetNoPackagesFound = -1978335212

# Hosting bundles by major.minor. WinGet only carries 3.1 and later, so 2.2 is downloaded directly.
$HostingBundles = @{
    '6.0' = @{ WinGetId = 'Microsoft.DotNet.HostingBundle.6' }
    '2.2' = @{ Url = 'https://dotnetcli.blob.core.windows.net/dotnet/aspnetcore/Runtime/2.2.8/dotnet-hosting-2.2.8-win.exe' }
}

function Get-InstalledProgram {
    # Add/Remove Programs entries (64-bit and 32-bit views) whose display name matches the wildcard.
    param([Parameter(Mandatory)][string]$DisplayName)

    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    @(Get-ItemProperty $keys -ErrorAction SilentlyContinue |
        Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -like $DisplayName })
}

function Install-WinGetPackage {
    # $LASTEXITCODE is winget's, except "already installed and up to date" -> 0.
    param([Parameter(Mandatory)][string]$Id)

    winget install --id $Id -e --source winget --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":$Id is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
    elseif ($LASTEXITCODE -ne 0) {
        Write-Output "!winget failed to install $Id (exit code $LASTEXITCODE)"
    }
}

function Uninstall-WinGetPackage {
    # $LASTEXITCODE is winget's, except "no such package installed" -> 0.
    param([Parameter(Mandatory)][string]$Id)

    winget uninstall --id $Id -e --source winget --silent
    if ($LASTEXITCODE -eq $WinGetNoPackagesFound) {
        Write-Output ":$Id is not installed"
        $global:LASTEXITCODE = 0
    }
    elseif ($LASTEXITCODE -ne 0) {
        Write-Output "!winget failed to uninstall $Id (exit code $LASTEXITCODE)"
    }
}

function Write-NotElevated {
    # Reports that the action needs Administrator and sets $LASTEXITCODE = 5; the caller then returns.
    param([string]$Action)

    Write-Output "!$Action requires running as Administrator."
    $global:LASTEXITCODE = 5
}

function Test-IisUrlRewrite {
    <#
    .SYNOPSIS
    Checks whether the IIS URL Rewrite module is installed. Sets $LASTEXITCODE to 0 if so, 2 if not.
    #>
    [CmdletBinding()]
    param()

    $program = Get-InstalledProgram 'IIS URL Rewrite Module*' | Select-Object -First 1
    if ($program -and (Get-InstalledIisVersion) -and (Test-IisModuleRegistered 'RewriteModule') -eq $false) {
        Write-Output "!$($program.DisplayName) is installed but isn't registered with IIS (IIS was reinstalled); reinstall it"
        $global:LASTEXITCODE = 2
    }
    elseif ($program) {
        Write-Output ":$($program.DisplayName) $($program.DisplayVersion) is installed"
        $global:LASTEXITCODE = 0
    }
    elseif (Test-Path (Join-Path $env:SystemRoot 'System32\inetsrv\rewrite.dll')) {
        Write-Output ":IIS URL Rewrite is installed"
        $global:LASTEXITCODE = 0
    }
    else {
        Write-Output "!IIS URL Rewrite is not installed"
        $global:LASTEXITCODE = 2
    }
}

function Install-IisUrlRewrite {
    <#
    .SYNOPSIS
    Installs the IIS URL Rewrite module via winget, if it isn't already installed.
    #>
    [CmdletBinding()]
    param()

    if (-not (Test-IsElevated)) { Write-NotElevated 'Installing IIS URL Rewrite'; return }
    if ((Get-InstalledProgram 'IIS URL Rewrite Module*') -and (Get-InstalledIisVersion) -and
        (Test-IisModuleRegistered 'RewriteModule') -eq $false) {
        # Present from before IIS was reinstalled: its registration lived in the old applicationHost.config.
        Write-Output ":IIS URL Rewrite isn't registered with IIS; reinstalling it"
        Uninstall-WinGetPackage 'Microsoft.IIS.URLRewrite'
        if ($LASTEXITCODE -ne 0) { return }
    }
    Install-WinGetPackage 'Microsoft.IIS.URLRewrite'
}

function Uninstall-IisUrlRewrite {
    <#
    .SYNOPSIS
    Uninstalls the IIS URL Rewrite module via winget. Succeeds if it isn't installed.
    #>
    [CmdletBinding()]
    param()

    if (-not (Test-IsElevated)) { Write-NotElevated 'Uninstalling IIS URL Rewrite'; return }
    Uninstall-WinGetPackage 'Microsoft.IIS.URLRewrite'
}

function Test-IisRequestRouting {
    <#
    .SYNOPSIS
    Checks whether IIS Application Request Routing (ARR) is installed. Sets $LASTEXITCODE to 0 if so, 2 if not.
    #>
    [CmdletBinding()]
    param()

    $program = Get-InstalledProgram 'Microsoft Application Request Routing*' | Select-Object -First 1
    if ($program -and (Get-InstalledIisVersion) -and (Test-IisModuleRegistered 'ApplicationRequestRouting') -eq $false) {
        Write-Output "!$($program.DisplayName) is installed but isn't registered with IIS (IIS was reinstalled); reinstall it"
        $global:LASTEXITCODE = 2
    }
    elseif ($program) {
        Write-Output ":$($program.DisplayName) $($program.DisplayVersion) is installed"
        $global:LASTEXITCODE = 0
    }
    elseif (Test-Path (Join-Path $env:ProgramFiles 'IIS\Application Request Routing\requestRouter.dll')) {
        Write-Output ":IIS Application Request Routing is installed"
        $global:LASTEXITCODE = 0
    }
    else {
        Write-Output "!IIS Application Request Routing is not installed"
        $global:LASTEXITCODE = 2
    }
}

function Install-IisRequestRouting {
    <#
    .SYNOPSIS
    Installs IIS Application Request Routing (ARR) via winget, if it isn't already installed. ARR needs URL Rewrite.
    #>
    [CmdletBinding()]
    param()

    if (-not (Test-IsElevated)) { Write-NotElevated 'Installing IIS Application Request Routing'; return }
    if ((Get-InstalledProgram 'Microsoft Application Request Routing*') -and (Get-InstalledIisVersion) -and
        (Test-IisModuleRegistered 'ApplicationRequestRouting') -eq $false) {
        Write-Output ":IIS Application Request Routing isn't registered with IIS; reinstalling it"
        Uninstall-WinGetPackage 'Microsoft.IIS.ApplicationRequestRouting'
        if ($LASTEXITCODE -ne 0) { return }
    }
    Install-WinGetPackage 'Microsoft.IIS.ApplicationRequestRouting'
}

function Uninstall-IisRequestRouting {
    <#
    .SYNOPSIS
    Uninstalls IIS Application Request Routing (ARR) via winget. Succeeds if it isn't installed.
    #>
    [CmdletBinding()]
    param()

    if (-not (Test-IsElevated)) { Write-NotElevated 'Uninstalling IIS Application Request Routing'; return }
    Uninstall-WinGetPackage 'Microsoft.IIS.ApplicationRequestRouting'
}

function Get-HostingBundleProgram {
    param([Parameter(Mandatory)][string]$Version)

    # The bundle's own entry is the only one named "... Windows Server Hosting"; the runtimes it
    # chains have other names and are removed along with it.
    Get-InstalledProgram '*Windows Server Hosting*' |
        Where-Object { $_.DisplayVersion -like "$Version.*" } |
        Select-Object -First 1
}

function Test-AspNetCoreHostingBundle {
    <#
    .SYNOPSIS
    Checks whether the ASP.NET Core Hosting Bundle for the given major.minor version (6.0 or 2.2) is installed.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if installed, 2 if not.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('6.0', '2.2')][string]$Version)

    $program = Get-HostingBundleProgram $Version
    if ($program -and (Get-InstalledIisVersion) -and (Test-IisModuleRegistered 'AspNetCoreModuleV2') -eq $false) {
        Write-Output "!$($program.DisplayName) is installed but its IIS module isn't registered (IIS was reinstalled); reinstall it"
        $global:LASTEXITCODE = 2
    }
    elseif ($program) {
        Write-Output ":$($program.DisplayName) ($($program.DisplayVersion)) is installed"
        $global:LASTEXITCODE = 0
    }
    else {
        Write-Output "!The ASP.NET Core $Version Hosting Bundle is not installed"
        $global:LASTEXITCODE = 2
    }
}

function Install-AspNetCoreHostingBundle {
    <#
    .SYNOPSIS
    Installs the ASP.NET Core Hosting Bundle for the given major.minor version (6.0 or 2.2), if it isn't already installed.
    .DESCRIPTION
    6.0 is installed via winget; 2.2 (out of support, not in winget) is downloaded from Microsoft's
    dotnetcli download host. Install IIS first so the ASP.NET Core Module is registered with it.
    A pending reboot is reported but counts as success.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('6.0', '2.2')][string]$Version)

    if (-not (Test-IsElevated)) { Write-NotElevated "Installing the ASP.NET Core $Version Hosting Bundle"; return }

    $existing = Get-HostingBundleProgram $Version
    if ($existing -and (Get-InstalledIisVersion) -and (Test-IisModuleRegistered 'AspNetCoreModuleV2') -eq $false) {
        # Installed before IIS was reinstalled: its module registration lived in the old applicationHost.config.
        Write-Output ":The ASP.NET Core $Version Hosting Bundle isn't registered with IIS; reinstalling it"
        Uninstall-AspNetCoreHostingBundle -Version $Version
        if ($LASTEXITCODE -ne 0) { return }
        $existing = $null
    }
    if ($existing) {
        Write-Output ":$($existing.DisplayName) ($($existing.DisplayVersion)) is already installed"
        $global:LASTEXITCODE = 0
        return
    }

    if (-not (Get-InstalledIisVersion)) {
        Write-Output ":IIS is not installed; the bundle's IIS module can't be registered until IIS is added and the bundle is repaired"
    }

    $bundle = $HostingBundles[$Version]
    if ($bundle.WinGetId) {
        Install-WinGetPackage $bundle.WinGetId
        return
    }

    $installer = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetFileName($bundle.Url))
    try {
        Write-Output ":Downloading $($bundle.Url)"
        $progress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        try { Invoke-WebRequest -Uri $bundle.Url -OutFile $installer -UseBasicParsing }
        finally { $ProgressPreference = $progress }

        Write-Output ":Installing $installer"
        $process = Start-Process -FilePath $installer -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
        $code = $process.ExitCode
    }
    catch {
        Write-Output "!Failed to install the ASP.NET Core $Version Hosting Bundle: $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }
    finally {
        Remove-Item $installer -Force -ErrorAction SilentlyContinue
    }

    if ($code -eq $DismRebootRequired) {
        Write-Output ":A restart is required to finish installing the hosting bundle"
        Write-Output "::RestartComputer"
        $code = 0
    }
    elseif ($code -ne 0) {
        Write-Output "!The installer failed with exit code $code"
    }
    $global:LASTEXITCODE = $code
}

function Uninstall-AspNetCoreHostingBundle {
    <#
    .SYNOPSIS
    Uninstalls the ASP.NET Core Hosting Bundle for the given major.minor version (6.0 or 2.2). Succeeds if it isn't installed.
    .DESCRIPTION
    Runs the bundle's own uninstaller, which also removes the runtimes it installed. A pending reboot is
    reported but counts as success.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('6.0', '2.2')][string]$Version)

    if (-not (Test-IsElevated)) { Write-NotElevated "Uninstalling the ASP.NET Core $Version Hosting Bundle"; return }

    $program = Get-HostingBundleProgram $Version
    if (-not $program) {
        Write-Output ":The ASP.NET Core $Version Hosting Bundle is not installed; nothing to remove"
        $global:LASTEXITCODE = 0
        return
    }

    $command = if ($program.QuietUninstallString) { $program.QuietUninstallString }
               else { "$($program.UninstallString) /quiet /norestart" }
    Write-Output ":Uninstalling $($program.DisplayName) ($($program.DisplayVersion))"
    $process = Start-Process -FilePath $env:ComSpec -ArgumentList '/c', "`"$command`"" -Wait -PassThru
    $code = $process.ExitCode

    if ($code -eq $DismRebootRequired) {
        Write-Output ":A restart is required to finish removing the hosting bundle"
        Write-Output "::RestartComputer"
        $code = 0
    }
    elseif ($code -ne 0) {
        Write-Output "!The uninstaller failed with exit code $code"
    }
    $global:LASTEXITCODE = $code
}

function Set-IisDefaultPermissions {
    <#
    .SYNOPSIS
    Restores the default IIS permissions on a web root, as on a clean IIS install.
    .DESCRIPTION
    Grants Read & Execute (inherited by subfolders and files) to IIS_IUSRS and Users.
    Only adds grants; nothing is removed. Fixes 401.3 errors from anonymous authentication.
    #>
    [CmdletBinding()]
    param(
        [string]$Path = 'C:\inetpub\wwwroot'
    )

    if (-not (Test-IsElevated)) { Write-NotElevated 'Setting IIS default permissions'; return }
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Output "!Path not found: $Path"
        $global:LASTEXITCODE = 1
        return
    }

    foreach ($grant in 'BUILTIN\IIS_IUSRS:(OI)(CI)(RX)', 'BUILTIN\Users:(OI)(CI)(RX)') {
        icacls $Path /grant $grant /T /C | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Output "!icacls failed to grant $grant on $Path (exit code $LASTEXITCODE)"
            return
        }
        Write-Output ":Granted $grant on $Path"
    }
    $global:LASTEXITCODE = 0
}

function Start-Iis {
    <#
    .SYNOPSIS
    Starts IIS (the World Wide Web Publishing Service and its dependency, the Windows Process Activation Service).
    #>
    [CmdletBinding()]
    param()

    if (-not (Test-IsElevated)) { Write-NotElevated 'Starting IIS'; return }
    if (-not (Get-Service W3SVC -ErrorAction SilentlyContinue)) {
        Write-Output '!IIS is not installed (W3SVC service not found).'
        $global:LASTEXITCODE = 2
        return
    }

    try {
        Start-Service W3SVC -ErrorAction Stop
    }
    catch {
        Write-Output "!Failed to start IIS: $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }
    Write-Output ':IIS started.'
    $global:LASTEXITCODE = 0
}

function Stop-Iis {
    <#
    .SYNOPSIS
    Stops IIS (the World Wide Web Publishing Service and the Windows Process Activation Service). Succeeds if it is already stopped.
    #>
    [CmdletBinding()]
    param()

    if (-not (Test-IsElevated)) { Write-NotElevated 'Stopping IIS'; return }
    if (-not (Get-Service W3SVC -ErrorAction SilentlyContinue)) {
        Write-Output '!IIS is not installed (W3SVC service not found).'
        $global:LASTEXITCODE = 2
        return
    }

    try {
        foreach ($name in 'W3SVC', 'WAS') {
            if (Get-Service $name -ErrorAction SilentlyContinue) {
                Stop-Service $name -Force -ErrorAction Stop
            }
        }
    }
    catch {
        Write-Output "!Failed to stop IIS: $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }
    Write-Output ':IIS stopped.'
    $global:LASTEXITCODE = 0
}

function Restart-Iis {
    <#
    .SYNOPSIS
    Restarts IIS: stops W3SVC and WAS, then starts W3SVC again (like iisreset).
    #>
    [CmdletBinding()]
    param()

    Stop-Iis
    if ($LASTEXITCODE -ne 0) { return }
    Start-Iis
}

Export-ModuleMember -Function Start-Iis, Stop-Iis, Restart-Iis, Install-Iis, Uninstall-Iis, Test-IisVersion,
    Test-IisUrlRewrite, Install-IisUrlRewrite, Uninstall-IisUrlRewrite,
    Test-IisRequestRouting, Install-IisRequestRouting, Uninstall-IisRequestRouting,
    Test-AspNetCoreHostingBundle, Install-AspNetCoreHostingBundle, Uninstall-AspNetCoreHostingBundle,
    Set-IisDefaultPermissions
