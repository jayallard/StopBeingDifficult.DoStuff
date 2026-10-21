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

function Copy-FileOverwrite {
    <#
    .SYNOPSIS
    Copies a file to a target path, overwriting the target if it already exists.
    .DESCRIPTION
    SourceFile must be an existing file. TargetFile is the full path of the copy (not a folder).
    Missing parent folders of TargetFile are created. An existing file at TargetFile is overwritten;
    a folder at TargetFile is never replaced.

    Sets $LASTEXITCODE to 0 on success, 1 on failure.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceFile,
        [Parameter(Mandatory)][string]$TargetFile
    )

    $global:LASTEXITCODE = 1

    if (-not (Test-Path -LiteralPath $SourceFile -PathType Leaf)) {
        Write-Output "!Source file not found: $SourceFile"
        return
    }
    $source = (Resolve-Path -LiteralPath $SourceFile).ProviderPath
    $targetFull = [System.IO.Path]::GetFullPath($TargetFile)

    if (Test-Path -LiteralPath $targetFull -PathType Container) {
        Write-Output "!$targetFull is a folder; TargetFile must be a file path."
        return
    }
    if ($source -eq $targetFull) {
        Write-Output "!Source and target are the same file: $source"
        return
    }

    $parent = Split-Path -Path $targetFull -Parent
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Write-Output ":Created folder $parent"
    }

    $existed = Test-Path -LiteralPath $targetFull -PathType Leaf
    try {
        Copy-Item -LiteralPath $source -Destination $targetFull -Force -ErrorAction Stop
    }
    catch {
        Write-Output "!Could not copy the file: $($_.Exception.Message)"
        return
    }

    $verb = if ($existed) { 'Overwrote' } else { 'Copied to' }
    Write-Output ":$verb $targetFull (from $source)"
    $global:LASTEXITCODE = 0
}

function Update-FileText {
    <#
    .SYNOPSIS
    Replaces every occurrence of a piece of text in a file.
    .DESCRIPTION
    FindText is matched literally (not as a regular expression) and every occurrence in FilePath is
    replaced with ReplaceText (which may be empty, to delete the text). Matching ignores case unless
    -CaseSensitive is set. The file's encoding (UTF-8 with or without BOM, UTF-16) is preserved, and
    the file is left untouched when there is nothing to replace.

    Sets $LASTEXITCODE to 0 on success (including "no matches"), 1 on failure.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$FindText,
        [string]$ReplaceText = '',
        [bool]$CaseSensitive = $false
    )

    $global:LASTEXITCODE = 1

    if ($FindText.Length -eq 0) {
        Write-Output '!FindText must not be empty.'
        return
    }
    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        Write-Output "!File not found: $FilePath"
        return
    }
    $path = (Resolve-Path -LiteralPath $FilePath).ProviderPath

    try {
        $bytes = [System.IO.File]::ReadAllBytes($path)
        $bomLength = 0
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            $encoding = New-Object System.Text.UTF8Encoding($true); $bomLength = 3
        }
        elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
            $encoding = New-Object System.Text.UnicodeEncoding($false, $true); $bomLength = 2
        }
        elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
            $encoding = New-Object System.Text.UnicodeEncoding($true, $true); $bomLength = 2
        }
        else {
            $encoding = New-Object System.Text.UTF8Encoding($false)
        }
        $text = $encoding.GetString($bytes, $bomLength, $bytes.Length - $bomLength)

        $options = [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
        if (-not $CaseSensitive) { $options = $options -bor [System.Text.RegularExpressions.RegexOptions]::IgnoreCase }
        $regex = New-Object System.Text.RegularExpressions.Regex([System.Text.RegularExpressions.Regex]::Escape($FindText), $options)

        $count = $regex.Matches($text).Count
        if ($count -eq 0) {
            Write-Output ":No occurrences of the text were found in $path; the file was not changed."
            $global:LASTEXITCODE = 0
            return
        }

        # A MatchEvaluator keeps '$' in ReplaceText literal instead of being read as a substitution.
        $newText = $regex.Replace($text, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $ReplaceText })
        [System.IO.File]::WriteAllText($path, $newText, $encoding)
    }
    catch {
        Write-Output "!Could not update the file: $($_.Exception.Message)"
        return
    }

    Write-Output ":Replaced $count occurrence(s) in $path"
    $global:LASTEXITCODE = 0
}

function New-Folder {
    <#
    .SYNOPSIS
    Ensures the folder at Path exists, creating any missing parts of the path.
    .DESCRIPTION
    Reports which leading parts of the path already existed and which were created. A path that
    already exists as a folder is success. It fails only if the path (or one of its parts) is an
    existing file, or a missing part cannot be created.

    Sets $LASTEXITCODE to 0 on success, 1 on failure.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path
    )

    $global:LASTEXITCODE = 1

    try {
        $full = [System.IO.Path]::GetFullPath($Path)
    }
    catch {
        Write-Output "!Invalid path '$Path': $($_.Exception.Message)"
        return
    }
    $root = [System.IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) { $full = $full.TrimEnd('\', '/') }

    # Walk up to the deepest part that already exists, collecting the missing parts below it.
    $missing = New-Object System.Collections.Generic.List[string]
    $current = $full
    while ($current -and -not (Test-Path -LiteralPath $current)) {
        $missing.Insert(0, $current)
        $current = Split-Path -Path $current -Parent
    }

    if (-not $current) {
        Write-Output "!Cannot create $full : its root '$root' does not exist."
        return
    }
    if (-not (Test-Path -LiteralPath $current -PathType Container)) {
        Write-Output "!Cannot create $full : $current exists but is a file, not a folder."
        return
    }

    Write-Output ":Already existed: $current"
    if ($missing.Count -eq 0) {
        $global:LASTEXITCODE = 0
        return
    }

    foreach ($part in $missing) {
        try {
            New-Item -ItemType Directory -Path $part -ErrorAction Stop | Out-Null
            Write-Output ":Created: $part"
        }
        catch {
            Write-Output "!Could not create $part : $($_.Exception.Message)"
            return
        }
    }
    $global:LASTEXITCODE = 0
}
