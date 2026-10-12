# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

function New-FolderLink {
    <#
    .SYNOPSIS
    Creates a symbolic link (or junction) at LinkPath that points to the existing folder TargetPath.
    .DESCRIPTION
    LinkPath is where the link is created; TargetPath is the real folder it maps to. Missing parent
    folders of LinkPath are created. If LinkPath is already a link to TargetPath, nothing is changed.
    If it is a link to somewhere else it is replaced only with -Force. A real (non-link) file or
    folder at LinkPath is never removed.

    A SymbolicLink needs an elevated session or Windows Developer Mode; a Junction needs neither but
    only works for folders on local volumes.

    Sets $LASTEXITCODE to 0 on success, 1 on failure.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LinkPath,
        [Parameter(Mandatory)][string]$TargetPath,
        [ValidateSet('SymbolicLink', 'Junction')][string]$LinkType = 'SymbolicLink',
        [bool]$Force = $false
    )

    $global:LASTEXITCODE = 1

    if (-not (Test-Path -LiteralPath $TargetPath -PathType Container)) {
        Write-Output "!Target folder not found: $TargetPath"
        return
    }
    $target = (Resolve-Path -LiteralPath $TargetPath).ProviderPath.TrimEnd('\', '/')

    $linkFull = [System.IO.Path]::GetFullPath($LinkPath).TrimEnd('\', '/')
    $existing = Get-Item -LiteralPath $linkFull -Force -ErrorAction SilentlyContinue
    if ($existing) {
        $isLink = $existing.LinkType -in @('SymbolicLink', 'Junction')
        if (-not $isLink) {
            Write-Output "!$linkFull already exists and is not a link; it will not be replaced."
            return
        }

        $currentTarget = @($existing.Target)[0]
        if ($currentTarget -and ($currentTarget.TrimEnd('\', '/') -eq $target)) {
            Write-Output ":$linkFull is already a $($existing.LinkType) to $target"
            $global:LASTEXITCODE = 0
            return
        }

        if (-not $Force) {
            Write-Output "!$linkFull is already a link to $currentTarget. Set Force to replace it."
            return
        }

        Write-Output ":Replacing existing link to $currentTarget"
        try {
            # Delete() removes only the link; Remove-Item on a directory link can recurse into the target in 5.1.
            $existing.Delete()
        }
        catch {
            Write-Output "!Could not remove the existing link: $($_.Exception.Message)"
            return
        }
    }

    $parent = Split-Path -Path $linkFull -Parent
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Write-Output ":Created folder $parent"
    }

    try {
        New-Item -ItemType $LinkType -Path $linkFull -Target $target -ErrorAction Stop | Out-Null
    }
    catch {
        Write-Output "!Could not create the ${LinkType}: $($_.Exception.Message)"
        if ($LinkType -eq 'SymbolicLink') {
            Write-Output "!Creating symbolic links needs an elevated session or Windows Developer Mode; a Junction does not."
        }
        return
    }

    Write-Output ":Created $LinkType $linkFull -> $target"
    $global:LASTEXITCODE = 0
}
