# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

function Set-EnvironmentVariable {
    <#
    .SYNOPSIS
    Persistently sets a Windows environment variable for the current user or the whole machine.
    .DESCRIPTION
    Windows only. The Machine type requires running as Administrator. Running processes (including
    this one) don't see the change; newly started processes do.
    Sets $LASTEXITCODE to 0 on success, 1 on failure, 2 if not running on Windows.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value,
        [Parameter(Mandatory)][string]$Type
    )

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        Write-Output "!Setting environment variables is only supported on Windows."
        $global:LASTEXITCODE = 2
        return
    }

    if ($Type -notin 'User', 'Machine') {
        Write-Output "!Type must be 'User' or 'Machine', not '$Type'."
        $global:LASTEXITCODE = 1
        return
    }

    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.Contains('=')) {
        Write-Output "!Name must be non-empty and must not contain '='."
        $global:LASTEXITCODE = 1
        return
    }

    try {
        [Environment]::SetEnvironmentVariable($Name, $Value, [EnvironmentVariableTarget]$Type)
    }
    catch {
        Write-Output "!Failed to set $Type environment variable '$Name': $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Set $Type environment variable $Name = $Value"
    $global:LASTEXITCODE = 0
}

function Get-EnvironmentVariable {
    <#
    .SYNOPSIS
    Displays the persisted value of a Windows environment variable for the current user or the whole machine.
    .DESCRIPTION
    Windows only. Reads the stored value, so it reflects changes made by processes that started earlier.
    Sets $LASTEXITCODE to 0 if the variable is set, 1 on bad input, 2 if not running on Windows or the variable isn't set.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Type
    )

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        Write-Output "!Reading persistent environment variables is only supported on Windows."
        $global:LASTEXITCODE = 2
        return
    }

    if ($Type -notin 'User', 'Machine') {
        Write-Output "!Type must be 'User' or 'Machine', not '$Type'."
        $global:LASTEXITCODE = 1
        return
    }

    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.Contains('=')) {
        Write-Output "!Name must be non-empty and must not contain '='."
        $global:LASTEXITCODE = 1
        return
    }

    $value = [Environment]::GetEnvironmentVariable($Name, [EnvironmentVariableTarget]$Type)
    if ($null -eq $value) {
        Write-Output "!$Type environment variable '$Name' is not set."
        $global:LASTEXITCODE = 2
        return
    }

    Write-Output ":$Type environment variable $Name = $value"
    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Set-EnvironmentVariable, Get-EnvironmentVariable
