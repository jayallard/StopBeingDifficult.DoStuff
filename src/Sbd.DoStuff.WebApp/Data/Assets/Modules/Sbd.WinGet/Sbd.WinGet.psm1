# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

function Test-WinGetVersion {
    <#
    .SYNOPSIS
    Checks that the installed WinGet is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if WinGet is older than MinVersion, or 2 if WinGet isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '1.29'
    )

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Output "!WinGet is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    $versionText = winget --version
    $match = [regex]::Match("$versionText", '\d+(?:\.\d+){1,3}')
    if (-not $match.Success) {
        Write-Output "!Could not parse the WinGet version from '$versionText'."
        $global:LASTEXITCODE = 1
        return
    }

    $version = [version]$match.Value
    Write-Output ":WinGet Version: $version"
    if ($version -lt $MinVersion) {
        Write-Output "!Failed: $version < MinVersion $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

function Install-WinGet {
    <#
    .SYNOPSIS
    Installs WinGet. Not implemented yet.
    #>
    [CmdletBinding()]
    param()

    Write-Output "!Not implemented yet. You should install it manually in the meantime."
    $global:LASTEXITCODE = 1
}

Export-ModuleMember -Function Test-WinGetVersion, Install-WinGet
