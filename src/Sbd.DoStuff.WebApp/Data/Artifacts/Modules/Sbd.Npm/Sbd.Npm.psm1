# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

function Invoke-NpmCommands {
    <#
    .SYNOPSIS
    Runs a list of npm commands (one per line) in a working directory, stopping at the first failure.
    .DESCRIPTION
    Version 'auto' reads the Node version from the working directory's .nvmrc and activates it with
    fnm; throws if there is no .nvmrc. Any other non-empty Version is activated with fnm as given.
    An empty Version uses whatever Node is already on PATH. Sets $LASTEXITCODE to 0 if every command
    succeeded, or to the exit code of the first command that failed.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Commands,
        [string]$Version = 'auto'
    )

    if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) {
        throw "Working directory '$WorkingDirectory' does not exist."
    }

    $lines = @($Commands -split "\r?\n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
    if ($lines.Count -eq 0) {
        throw 'No commands were supplied.'
    }

    Push-Location -LiteralPath $WorkingDirectory
    try {
        $version = "$Version".Trim()
        if ($version -eq 'auto') {
            $nvmrc = Join-Path (Get-Location).Path '.nvmrc'
            if (-not (Test-Path -LiteralPath $nvmrc -PathType Leaf)) {
                throw "Version is 'auto' but no .nvmrc file exists in '$WorkingDirectory'."
            }
            $version = (Get-Content -LiteralPath $nvmrc -TotalCount 1).Trim()
            if (-not $version) { throw ".nvmrc in '$WorkingDirectory' is empty." }
            Write-Output ":Using Node version '$version' from .nvmrc"
        }

        if ($version) {
            if (-not (Get-Command fnm -ErrorAction SilentlyContinue)) { throw 'fnm is not installed.' }
            fnm env --shell powershell | Out-String | Invoke-Expression
            fnm use $version --install-if-missing
            if ($LASTEXITCODE -ne 0) { throw "fnm could not switch to Node version '$version' (exit code $LASTEXITCODE)." }
        }

        foreach ($line in $lines) {
            Write-Output ":> $line"
            $global:LASTEXITCODE = 0
            & ([scriptblock]::Create($line))
            $exitCode = $LASTEXITCODE
            if ($exitCode -ne 0) {
                Write-Output "!Command failed with exit code ${exitCode}: $line"
                $global:LASTEXITCODE = $exitCode
                return
            }
        }

        Write-Output ":All $($lines.Count) command(s) succeeded"
        $global:LASTEXITCODE = 0
    }
    finally {
        Pop-Location
    }
}

Export-ModuleMember -Function Invoke-NpmCommands
