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

function Invoke-GitCloneWithProgress {
    # Runs `git clone --progress` and streams its output as it happens, returning git's exit code.
    # Git only reports progress when asked to (stderr is not a terminal here), and it rewrites the
    # progress line in place using carriage returns, so stderr is read character by character and
    # split on both \r and \n. Progress updates are throttled to keep the run's output readable.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Url,
        [Parameter(Mandatory)]
        [string]$Target,
        # Extra `git clone` options, e.g. --depth 1. Must not contain anything needing quoting.
        [string]$CloneOptions = ''
    )

    $script:GitCloneNoCommitsInRange = $false

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.Arguments = 'clone --progress {0} "{1}" "{2}"' -f $CloneOptions, $Url.Replace('"', '\"'), $Target.Replace('"', '\"')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    try {
        [void]$process.Start()
        # git clone writes nothing useful to stdout; drain it so the child can never block on a full pipe.
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()

        $reader = $process.StandardError
        $buffer = New-Object System.Text.StringBuilder
        $lastKey = $null
        $lastEmit = [DateTime]::MinValue
        $throttle = [TimeSpan]::FromSeconds(2)

        $emit = {
            param([string]$text)
            $text = $text.Trim()
            if (-not $text) { return }
            # An empty --shallow-since window is reported as either of these, depending on git/server version.
            if ($text -match 'no commits selected for shallow requests|error processing shallow info') { $script:GitCloneNoCommitsInRange = $true }

            if ($text -match '^(?<key>.+?):\s+(?<pct>\d+)%') {
                # A progress update. Show it when the phase changes, when it completes, or after the throttle interval.
                $now = [DateTime]::UtcNow
                if ($Matches.key -ne $lastKey -or $Matches.pct -eq '100' -or ($now - $lastEmit) -ge $throttle) {
                    Write-Output $text
                    $lastKey = $Matches.key
                    $lastEmit = $now
                }
            }
            else {
                Write-Output $text
            }
        }

        while (($code = $reader.Read()) -ge 0) {
            $ch = [char]$code
            if ($ch -eq "`r" -or $ch -eq "`n") {
                if ($buffer.Length -gt 0) {
                    . $emit $buffer.ToString()
                    [void]$buffer.Clear()
                }
            }
            else {
                [void]$buffer.Append($ch)
            }
        }
        if ($buffer.Length -gt 0) {
            . $emit $buffer.ToString()
        }

        $process.WaitForExit()
        [void]$stdoutTask.Result
        $global:LASTEXITCODE = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
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
    .PARAMETER HistoryDays
    Only clone commits from the last N days (a shallow clone). 0, the default, clones the full history. A
    repository with no commits in that window is cloned with just its latest commit.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$WorkDirectory,
        [Parameter(Mandatory)]
        [string]$Repositories,
        [ValidateRange(0, 36500)]
        [int]$HistoryDays = 0
    )

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Output "!Git is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    $cloneOptions = ''
    if ($HistoryDays -gt 0) {
        $since = (Get-Date).AddDays(-$HistoryDays).ToString('yyyy-MM-dd')
        # --shallow-since implies --single-branch; --no-single-branch keeps every branch available to check out.
        $cloneOptions = "--shallow-since=$since --no-single-branch"
        Write-Output ":Cloning history since $since (last $HistoryDays days)"
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
        Invoke-GitCloneWithProgress -Url $url -Target $target -CloneOptions $cloneOptions
        if ($LASTEXITCODE -ne 0 -and $HistoryDays -gt 0 -and $script:GitCloneNoCommitsInRange) {
            # An inactive repository has nothing in the window; fall back to just its latest commit.
            Write-Output ":No commits in the last $HistoryDays days in $url - cloning the latest commit only"
            Invoke-GitCloneWithProgress -Url $url -Target $target -CloneOptions '--depth 1 --no-single-branch'
        }
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

function Enable-GitLongPaths {
    <#
    .SYNOPSIS
    Sets git's core.longpaths for the current user, so paths over 259 characters can be checked out on Windows.
    .DESCRIPTION
    Writes core.longpaths=true to the user's global git config (idempotent). Without it, git silently skips
    files whose full path exceeds Windows' MAX_PATH. Sets $LASTEXITCODE to 0 on success, 2 if Git isn't installed.
    #>
    [CmdletBinding()]
    param()

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Output "!Git is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    $current = git config --global --get core.longpaths
    if ($current -eq 'true') {
        Write-Output ":core.longpaths is already enabled"
        $global:LASTEXITCODE = 0
        return
    }

    git config --global core.longpaths true
    if ($LASTEXITCODE -ne 0) {
        Write-Output "!Failed to set core.longpaths (git exit code $LASTEXITCODE)"
        return
    }
    Write-Output ":Enabled core.longpaths in the global git config"
}

Export-ModuleMember -Function Install-Git, Test-GitVersion, Copy-GitRepository, Enable-GitLongPaths
