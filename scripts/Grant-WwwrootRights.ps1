#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Restores the default IIS ACLs on a web root, as on a clean IIS install.

.DESCRIPTION
    A clean install grants these on C:\inetpub\wwwroot (inherited by children):
        BUILTIN\IIS_IUSRS   Read & Execute  (OI)(CI)
        BUILTIN\Users       Read & Execute  (OI)(CI)
        BUILTIN\Administrators, SYSTEM, TrustedInstaller   Full control
    This script (re)applies the IIS_IUSRS and Users grants, and optionally IUSR
    explicitly, which fixes 401.3 errors from anonymous authentication.

.EXAMPLE
    .\Grant-WwwrootRights.ps1
    .\Grant-WwwrootRights.ps1 -Path D:\sites\rpo -IncludeIusr
#>
param(
    [string]$Path = 'C:\inetpub\wwwroot',
    [switch]$IncludeIusr
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) { throw "Path not found: $Path" }

# (OI)(CI)(RX) = this folder, subfolders and files; read & execute.
$grants = @('BUILTIN\IIS_IUSRS:(OI)(CI)(RX)', 'BUILTIN\Users:(OI)(CI)(RX)')
if ($IncludeIusr) { $grants += 'NT AUTHORITY\IUSR:(OI)(CI)(RX)' }

foreach ($grant in $grants) {
    Write-Host "icacls $Path /grant $grant"
    icacls $Path /grant $grant /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed for $grant (exit $LASTEXITCODE)" }
}

icacls $Path
