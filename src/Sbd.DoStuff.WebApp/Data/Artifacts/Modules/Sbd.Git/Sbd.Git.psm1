# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

$WinGetNoApplicableUpgrade = -1978335189

function Install-Git {
    <#
    .SYNOPSIS
    Installs Git via winget, if it isn't already installed.
    .DESCRIPTION
    Sets $LASTEXITCODE to winget's exit code, or 0 if Git is already installed and up to date.
    #>
    [CmdletBinding()]
    param()

    winget install --id Git.Git -e --source winget
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":Git is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
}

function Test-GitVersion {
    <#
    .SYNOPSIS
    Checks that the installed Git CLI is at least the given version.
    .DESCRIPTION
    Sets $LASTEXITCODE to 0 if it passes, 1 if Git is older than MinVersion, or 2 if Git isn't installed.
    #>
    [CmdletBinding()]
    param(
        [version]$MinVersion = '2.53.0'
    )

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Output "!Git is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    $gitVersion = git --version
    if ($gitVersion -notmatch '\b(\d+\.\d+\.\d+)\b') {
        Write-Output "!Could not parse the Git version from '$gitVersion'."
        $global:LASTEXITCODE = 1
        return
    }

    $version = [version]$Matches[1]
    Write-Output ":Git Version: $version"
    if ($version -lt $MinVersion) {
        Write-Output "!Git Version validation failed. $version < $MinVersion"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Passed: $version >= $MinVersion"
    $global:LASTEXITCODE = 0
}

function Copy-GitRepository {
    <#
    .SYNOPSIS
    Clones each of the given repositories into a work directory.
    .DESCRIPTION
    Each repository is cloned into a folder under WorkDirectory named after the repository (the last
    segment of its URL, without any .git suffix). A repository whose folder already exists is reported
    and skipped, not treated as an error. Every repository is attempted even if an earlier clone fails.
    Sets $LASTEXITCODE to 0 if all repositories were cloned or already existed, or 1 if any clone failed.
    .PARAMETER Repositories
    Repository URLs (or paths), one per line. Lines are trimmed; blank lines and lines starting with # are ignored.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkDirectory,
        [Parameter(Mandatory)]
        [string]$Repositories
    )

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Output "!Git is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    $urls = @($Repositories -split '[\r\n]+' | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
    if ($urls.Count -eq 0) {
        Write-Output "!No repositories were specified."
        $global:LASTEXITCODE = 1
        return
    }

    if (-not (Test-Path -LiteralPath $WorkDirectory)) {
        New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
        Write-Output ":Created work directory $WorkDirectory"
    }

    $failed = 0
    foreach ($url in $urls) {
        $name = ($url.TrimEnd('/', '\') -split '[/\\:]')[-1] -replace '\.git$', ''
        if (-not $name) {
            Write-Output "!Could not determine a folder name for '$url'."
            $failed++
            continue
        }

        $target = Join-Path $WorkDirectory $name
        if (Test-Path -LiteralPath $target) {
            Write-Output ":Skipped $url - $target already exists"
            continue
        }

        Write-Output ":Cloning $url into $target"
        git clone $url $target 2>&1 | ForEach-Object { Write-Output "$_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Output "!Failed to clone $url (git exit code $LASTEXITCODE)"
            $failed++
        }
        else {
            Write-Output ":Cloned $url"
        }
    }

    if ($failed -gt 0) {
        Write-Output "!$failed of $($urls.Count) repositories failed to clone."
        $global:LASTEXITCODE = 1
    }
    else {
        $global:LASTEXITCODE = 0
    }
}

Export-ModuleMember -Function Install-Git, Test-GitVersion, Copy-GitRepository
