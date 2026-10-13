# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

$WinGetNoApplicableUpgrade = -1978335189

function Update-SessionPath {
    # winget updates the persisted PATH, but this process keeps the PATH it started with, so a
    # freshly installed dotnet wouldn't be found until the app restarts. Re-read it from the registry.
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user, $env:Path) | Where-Object { $_ }) -join ';'
}

function Get-DotnetSdks {
    <#
    .SYNOPSIS
    Lists the installed .NET SDKs.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if at least one SDK is installed, or 1 if dotnet or its SDKs are missing.
    #>
    [CmdletBinding()]
    param()

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Update-SessionPath }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Output "!dotnet is not installed."
        $global:LASTEXITCODE = 1
        return
    }

    $sdks = @(dotnet --list-sdks | Where-Object { $_ })
    if ($sdks.Count -eq 0) {
        Write-Output "!No .NET SDKs are installed."
        $global:LASTEXITCODE = 1
        return
    }

    foreach ($sdk in $sdks) { Write-Output ":$sdk" }
    $global:LASTEXITCODE = 0
}

function Install-DotnetSdk {
    <#
    .SYNOPSIS
    Installs a .NET SDK via winget, if it isn't already installed.
    .DESCRIPTION
    Major is the major version (e.g. 8). Versions that winget has no stable package for yet (such as
    11) fall back to the Microsoft.DotNet.SDK.Preview package. Sets $LASTEXITCODE to winget's exit
    code, or 0 if the SDK is already installed and up to date.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [int]$Major
    )

    $id = "Microsoft.DotNet.SDK.$Major"
    $found = winget search --id $id -e --source winget 2>&1 | Out-String
    if ($found -notmatch [regex]::Escape($id)) {
        Write-Output ":No stable winget package for .NET $Major SDK; using the preview package."
        $id = 'Microsoft.DotNet.SDK.Preview'
    }

    winget install --id $id -e --source winget
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":.NET $Major SDK is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
    if ($LASTEXITCODE -eq 0) { Update-SessionPath }
}

# SDKs that winget doesn't carry (it starts at 3.1), by major.minor. They're out of support but
# Microsoft still hosts the installers.
$LegacySdks = @{
    '2.2' = @{
        Version = '2.2.207'
        Url     = 'https://dotnetcli.blob.core.windows.net/dotnet/Sdk/2.2.207/dotnet-sdk-2.2.207-win-x64.exe'
    }
}

function Install-DotnetLegacySdk {
    <#
    .SYNOPSIS
    Installs an out-of-support .NET Core SDK (2.2) by downloading Microsoft's installer directly.
    .DESCRIPTION
    winget has no package for these versions. Needs an elevated session. Sets $LASTEXITCODE to the
    installer's exit code, or 0 if the SDK is already installed (a pending reboot counts as success).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('2.2')]
        [string]$Version
    )

    $sdk = $LegacySdks[$Version]

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Update-SessionPath }
    if ((Get-Command dotnet -ErrorAction SilentlyContinue) -and
        (@(dotnet --list-sdks) | Where-Object { $_ -like "$($sdk.Version) *" })) {
        Write-Output ":.NET Core SDK $($sdk.Version) is already installed"
        $global:LASTEXITCODE = 0
        return
    }

    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Output "!Installing the .NET Core SDK $($sdk.Version) requires an elevated (administrator) session."
        $global:LASTEXITCODE = 1
        return
    }

    $installer = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetFileName($sdk.Url))
    try {
        Write-Output ":Downloading $($sdk.Url)"
        $progress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        try { Invoke-WebRequest -Uri $sdk.Url -OutFile $installer -UseBasicParsing }
        finally { $ProgressPreference = $progress }

        Write-Output ":Installing $installer"
        $process = Start-Process -FilePath $installer -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
        $code = $process.ExitCode
    }
    catch {
        Write-Output "!Failed to install the .NET Core SDK $($sdk.Version): $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }
    finally {
        Remove-Item $installer -Force -ErrorAction SilentlyContinue
    }

    if ($code -eq 3010) {
        Write-Output ":A restart is required to finish installing the SDK"
        $code = 0
    }
    elseif ($code -ne 0) {
        Write-Output "!The installer failed with exit code $code"
    }
    else {
        Update-SessionPath
    }
    $global:LASTEXITCODE = $code
}

function Find-MsBuild {
    # `dotnet build` can't compile .sqlproj (SSDT) projects, which need the full Visual Studio MSBuild.
    # Prefer the newest VS install that has MSBuild (via vswhere), then fall back to PATH.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $found = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($found) { return $found }
    }
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Build-DotnetSolution {
    <#
    .SYNOPSIS
    Compiles a solution (or project) with `dotnet build`, or with MSBuild when -UseMsBuild is set.
    .DESCRIPTION
    SolutionPath is the .sln/.slnx/.csproj/.sqlproj file, or a directory containing exactly one.
    Configuration is Debug or Release. UseMsBuild compiles with Visual Studio's MSBuild.exe instead
    of dotnet (needed for .sqlproj). Sets $LASTEXITCODE to the build tool's exit code (0 = succeeded).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$SolutionPath,

        [ValidateSet('Debug', 'Release')]
        [string]$Configuration = 'Debug',

        [switch]$UseMsBuild
    )

    if (-not (Test-Path -LiteralPath $SolutionPath)) {
        Write-Output "!Path not found: $SolutionPath"
        $global:LASTEXITCODE = 1
        return
    }

    if ($UseMsBuild) {
        $msbuild = Find-MsBuild
        if (-not $msbuild) {
            Write-Output "!MSBuild was not found. Install Visual Studio (or the Build Tools) with the MSBuild component."
            $global:LASTEXITCODE = 1
            return
        }

        Write-Output ":Building $SolutionPath ($Configuration) with MSBuild: $msbuild"
        & $msbuild $SolutionPath /restore /p:Configuration=$Configuration /nologo /verbosity:minimal
    } else {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Update-SessionPath }
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            Write-Output "!dotnet is not installed."
            $global:LASTEXITCODE = 1
            return
        }

        Write-Output ":Building $SolutionPath ($Configuration)"
        dotnet build $SolutionPath --configuration $Configuration --nologo
    }

    if ($LASTEXITCODE -eq 0) {
        Write-Output ":Build succeeded."
    } else {
        Write-Output "!Build failed (exit code $LASTEXITCODE)."
    }
}

Export-ModuleMember -Function Get-DotnetSdks, Install-DotnetSdk, Install-DotnetLegacySdk, Build-DotnetSolution
