# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

function Invoke-Wsl {
    # wsl.exe writes UTF-16 to redirected output; decode it as such and drop any stray NULs.
    # Leaves $LASTEXITCODE as wsl's own exit code.
    param([string[]]$Arguments)

    $previous = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [System.Text.Encoding]::Unicode
        $output = & wsl.exe @Arguments 2>&1 | Out-String
    }
    finally {
        [Console]::OutputEncoding = $previous
    }
    ($output -replace "`0", '').Trim()
}

function Install-Wsl {
    <#
    .SYNOPSIS
    Installs the Windows Subsystem for Linux (without a distribution) if it isn't already installed.
    .DESCRIPTION
    Runs 'wsl --install --no-distribution', which requires an elevated session. A restart may be
    needed before WSL is usable. Sets $LASTEXITCODE to 0 on success.
    #>
    [CmdletBinding()]
    param()

    if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
        Write-Output "!wsl.exe was not found. WSL requires Windows 10 version 2004 or later, or Windows 11."
        $global:LASTEXITCODE = 1
        return
    }

    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Output "!Installing WSL requires an elevated (administrator) session."
        $global:LASTEXITCODE = 1
        return
    }

    $null = Invoke-Wsl '--version'
    if ($LASTEXITCODE -eq 0) {
        Write-Output ":WSL is already installed; updating it"
        $text = Invoke-Wsl '--update'
    }
    else {
        Write-Output ":Installing WSL"
        $text = Invoke-Wsl '--install', '--no-distribution'
    }

    $code = $LASTEXITCODE
    if ($text) { Write-Output $text }
    if ($code -ne 0) {
        Write-Output "!wsl.exe failed with exit code $code."
        $global:LASTEXITCODE = $code
        return
    }

    Write-Output ":WSL installed; restart Windows if it reported that a restart is required"
    $global:LASTEXITCODE = 0
}

function Enable-VirtualMachinePlatform {
    <#
    .SYNOPSIS
    Enables the Windows Virtual Machine Platform optional feature (needed by WSL 2).
    .DESCRIPTION
    Runs 'dism.exe /online /enable-feature', which requires an elevated session. A restart may be
    needed before the platform is usable. Sets $LASTEXITCODE to 0 on success (including when the
    feature was already enabled).
    #>
    [CmdletBinding()]
    param()

    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Output "!Enabling the Virtual Machine Platform requires an elevated (administrator) session."
        $global:LASTEXITCODE = 1
        return
    }

    $info = & dism.exe /online /get-featureinfo /featurename:VirtualMachinePlatform 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -and $info -match '(?im)^\s*State\s*:\s*Enabled\s*$') {
        Write-Output ":Virtual Machine Platform is already enabled"
        $global:LASTEXITCODE = 0
        return
    }

    Write-Output ":Enabling Virtual Machine Platform"
    $text = & dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart 2>&1 | Out-String
    $code = $LASTEXITCODE
    $text = $text.Trim()
    if ($text) { Write-Output $text }

    # 3010 = success, restart required.
    if ($code -ne 0 -and $code -ne 3010) {
        Write-Output "!dism.exe failed with exit code $code."
        $global:LASTEXITCODE = $code
        return
    }

    Write-Output ":Virtual Machine Platform enabled; restart Windows to finish applying it"
    $global:LASTEXITCODE = 0
}

function Test-WslVersion {
    <#
    .SYNOPSIS
    Checks that the installed WSL is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if WSL is older than MinVersion, or 2 if it isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '2.0.0'
    )

    if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
        Write-Output "!WSL is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    # e.g. "WSL version: 2.3.26.0". Older inbox WSL doesn't support --version at all.
    $versionText = Invoke-Wsl '--version'
    if ($LASTEXITCODE -ne 0 -or $versionText -notmatch '(?i)WSL[^:\r\n]*:\s*v?(\d+(\.\d+){1,3})') {
        Write-Output "!WSL is not installed, or is too old to report a version."
        $global:LASTEXITCODE = 2
        return
    }

    $version = [version]$Matches[1]
    Write-Output ":WSL Version: $version"
    if ($version -lt $MinVersion) {
        Write-Output "!WSL Version validation failed. $version < $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Install-Wsl, Enable-VirtualMachinePlatform, Test-WslVersion
