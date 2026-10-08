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

Export-ModuleMember -Function Get-DotnetSdks, Install-DotnetSdk
