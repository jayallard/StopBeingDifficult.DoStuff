# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

function Set-XmlValue {
    <#
    .SYNOPSIS
    Sets the value of every node in an XML file that matches an XPath expression.
    .DESCRIPTION
    An element match gets its text content replaced; an attribute (or text/CDATA) match gets its value
    replaced. The file's existing whitespace and declared encoding are preserved. The XPath is evaluated
    without namespace prefixes, so for documents with a default namespace use local-name(), e.g.
    //*[local-name()='add'][@key='Foo']/@value.
    Sets $LASTEXITCODE to 0 on success, 1 on failure (bad input, unreadable/invalid XML, or no node matched),
    2 if the file doesn't exist.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$XPath,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    if (-not (Test-Path -LiteralPath $FileName -PathType Leaf)) {
        Write-Output "!XML file not found: $FileName"
        $global:LASTEXITCODE = 2
        return
    }

    $fullPath = (Resolve-Path -LiteralPath $FileName).ProviderPath

    try {
        $doc = New-Object System.Xml.XmlDocument
        $doc.PreserveWhitespace = $true
        $doc.Load($fullPath)

        $nodes = $doc.SelectNodes($XPath)
    }
    catch {
        Write-Output "!Failed to read '$fullPath' or evaluate XPath '$XPath': $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }

    if ($nodes.Count -eq 0) {
        Write-Output "!XPath '$XPath' matched no nodes in $fullPath"
        $global:LASTEXITCODE = 1
        return
    }

    foreach ($node in $nodes) {
        if ($node -is [System.Xml.XmlElement]) {
            $node.InnerText = $Value
        }
        else {
            $node.Value = $Value
        }
    }

    try {
        # Save with the document's declared encoding, keeping a BOM only if the original had one.
        $head = [System.IO.File]::ReadAllBytes($fullPath) | Select-Object -First 3
        $hasBom = ($head.Count -ge 3 -and $head[0] -eq 0xEF -and $head[1] -eq 0xBB -and $head[2] -eq 0xBF) -or
                  ($head.Count -ge 2 -and (($head[0] -eq 0xFF -and $head[1] -eq 0xFE) -or ($head[0] -eq 0xFE -and $head[1] -eq 0xFF)))
        $encodingName = if ($doc.FirstChild -is [System.Xml.XmlDeclaration] -and $doc.FirstChild.Encoding) { $doc.FirstChild.Encoding } else { 'utf-8' }
        $encoding = [System.Text.Encoding]::GetEncoding($encodingName)
        if ($encoding -is [System.Text.UTF8Encoding]) { $encoding = New-Object System.Text.UTF8Encoding($hasBom) }
        $writer = New-Object System.IO.StreamWriter($fullPath, $false, $encoding)
        try { $doc.Save($writer) } finally { $writer.Dispose() }
    }
    catch {
        Write-Output "!Failed to save '$fullPath': $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }

    Write-Output ":Set $($nodes.Count) node(s) matching '$XPath' to '$Value' in $fullPath"
    $global:LASTEXITCODE = 0
}

function ConvertFrom-SettingCommands {
    # Parses multiline commands, one per line: "set <key> <value>" or "delete <key>" (blank and '#' lines skipped).
    # The command word is case-insensitive; the key is the next whitespace-delimited word and, for set, the value
    # is the rest of the line (trimmed, may be empty). Returns objects with Command, Key and Value; throws on a
    # malformed line or when there are none.
    param([string]$Text)

    $commands = @()
    foreach ($line in ($Text -split "\r?\n")) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        if ($trimmed -match '^(?<cmd>\S+)\s+(?<key>\S+)(?:\s+(?<value>.*))?$') {
            $command = $Matches['cmd'].ToLowerInvariant()
            $key = $Matches['key']
            $value = if ($Matches['value']) { $Matches['value'].Trim() } else { '' }
            if ($command -eq 'set') {
                $commands += [pscustomobject]@{ Command = 'set'; Key = $key; Value = $value }
                continue
            }
            if ($command -eq 'delete' -and $value -eq '') {
                $commands += [pscustomobject]@{ Command = 'delete'; Key = $key; Value = $null }
                continue
            }
            if ($command -eq 'delete') {
                throw "invalid line (delete takes only a key): $trimmed"
            }
        }
        throw "invalid line (expected 'set key value' or 'delete key'): $trimmed"
    }
    if ($commands.Count -eq 0) {
        throw 'no commands supplied'
    }
    # The comma prevents PowerShell from unrolling a single-element array into a bare object.
    return , $commands
}

function Save-XmlDocument {
    # Saves with the document's declared encoding, keeping a BOM only if the original file had one.
    param($Document, [string]$Path)

    try {
        $head = [System.IO.File]::ReadAllBytes($Path) | Select-Object -First 3
        $hasBom = ($head.Count -ge 3 -and $head[0] -eq 0xEF -and $head[1] -eq 0xBB -and $head[2] -eq 0xBF) -or
                  ($head.Count -ge 2 -and (($head[0] -eq 0xFF -and $head[1] -eq 0xFE) -or ($head[0] -eq 0xFE -and $head[1] -eq 0xFF)))
        $encodingName = if ($Document.FirstChild -is [System.Xml.XmlDeclaration] -and $Document.FirstChild.Encoding) { $Document.FirstChild.Encoding } else { 'utf-8' }
        $encoding = [System.Text.Encoding]::GetEncoding($encodingName)
        if ($encoding -is [System.Text.UTF8Encoding]) { $encoding = New-Object System.Text.UTF8Encoding($hasBom) }
        $writer = New-Object System.IO.StreamWriter($Path, $false, $encoding)
        try { $Document.Save($writer) } finally { $writer.Dispose() }
    }
    catch {
        throw "failed to save '$Path': $($_.Exception.Message)"
    }
}

function Set-WebConfigAppSettings {
    <#
    .SYNOPSIS
    Adds, updates or deletes key/value entries of an appSettings section in a web.config (or extension) file.
    .DESCRIPTION
    Settings is a multiline string of commands, one per line: "set key value" or "delete key". The first word is
    the command (case-insensitive), the key is the next word, and for set the value is the rest of the line
    (trimmed). Blank lines and lines starting with '#' are ignored. Set updates an existing key or adds a missing
    one; delete removes the key if present. Each command is reported as added, updated, unchanged, deleted or
    not found.
    The file must contain exactly one appSettings element (at any depth, including the root of an extension
    file); zero or several is an error. Keys are matched case-sensitively. The file's existing whitespace and
    declared encoding are preserved, and it is only rewritten if something changed.
    Sets $LASTEXITCODE to 0 on success, 1 on failure (bad input, unreadable/invalid XML, or not exactly one
    appSettings element), 2 if the file doesn't exist.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$Settings
    )

    if (-not (Test-Path -LiteralPath $FileName -PathType Leaf)) {
        Write-Output "!web.config not found: $FileName"
        $global:LASTEXITCODE = 2
        return
    }

    try { $commands = ConvertFrom-SettingCommands -Text $Settings }
    catch {
        Write-Output "!Invalid Settings: $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }

    $fullPath = (Resolve-Path -LiteralPath $FileName).ProviderPath
    $changed = $false

    try {
        $doc = New-Object System.Xml.XmlDocument
        $doc.PreserveWhitespace = $true
        $doc.Load($fullPath)

        $sections = @($doc.SelectNodes('//*') | Where-Object { $_.LocalName -ieq 'appSettings' })
        if ($sections.Count -ne 1) {
            throw "expected exactly one appSettings element but found $($sections.Count)"
        }
        $section = $sections[0]

        $keyAttribute = 'key'; $valueAttribute = 'value'

        foreach ($cmd in $commands) {
            $existing = $null
            foreach ($node in $section.SelectNodes("*[local-name()='add']")) {
                if ($node.GetAttribute($keyAttribute) -eq $cmd.Key) { $existing = $node; break }
            }
            if ($cmd.Command -eq 'delete') {
                if ($null -eq $existing) {
                    Write-Output ":Not deleted: no setting with the key $($cmd.Key) exists."
                }
                else {
                    $old = $existing.GetAttribute($valueAttribute)
                    [void]$section.RemoveChild($existing)
                    $changed = $true
                    Write-Output ":Deleted the setting $($cmd.Key) (its value was $old)"
                }
            }
            elseif ($null -eq $existing) {
                $add = $doc.CreateElement('add', $section.NamespaceURI)
                $add.SetAttribute($keyAttribute, $cmd.Key)
                $add.SetAttribute($valueAttribute, $cmd.Value)
                [void]$section.AppendChild($add)
                $changed = $true
                Write-Output ":Created the setting $($cmd.Key) = $($cmd.Value)"
            }
            elseif ($existing.GetAttribute($valueAttribute) -eq $cmd.Value) {
                Write-Output ":Not updated: the setting $($cmd.Key) already has the value $($cmd.Value)."
            }
            else {
                $old = $existing.GetAttribute($valueAttribute)
                $existing.SetAttribute($valueAttribute, $cmd.Value)
                $changed = $true
                Write-Output ":Updated the setting $($cmd.Key). Changed value from $old to $($cmd.Value)"
            }
        }
    }
    catch {
        Write-Output "!Failed to read or update '$fullPath': $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }

    if (-not $changed) {
        Write-Output ':No changes were needed; the file was not modified.'
        $global:LASTEXITCODE = 0
        return
    }

    try { Save-XmlDocument -Document $doc -Path $fullPath }
    catch {
        Write-Output "!$($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }

    $global:LASTEXITCODE = 0
}

function Set-WebConfigConnectionStrings {
    <#
    .SYNOPSIS
    Adds, updates or deletes <add> entries in the connectionStrings section of a web.config (or extension) file.
    .DESCRIPTION
    ConnectionStrings is a multiline string of commands, one per line: "set name connection string" or
    "delete name". The first word is the command (case-insensitive), the name is the next word, and for set the
    connection string is the rest of the line (trimmed, may contain spaces and '='). Blank lines and lines
    starting with '#' are ignored. The file must contain exactly one connectionStrings element (at any depth,
    including the root of an extension file); zero or several is an error. Entries are matched by name
    (case-sensitive). Set adds a missing entry with the given ProviderName, or updates an existing one's
    connectionString and providerName to match; delete removes the entry if present. Each command is reported as
    created, updated, not updated, deleted or not deleted. The file's existing whitespace and declared encoding
    are preserved, and it is only rewritten if something changed.
    Sets $LASTEXITCODE to 0 on success, 1 on failure (bad input, unreadable/invalid XML, or not exactly one
    connectionStrings element), 2 if the file doesn't exist.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$ConnectionStrings,
        [string]$ProviderName = 'Microsoft.Data.SqlClient'
    )

    if (-not (Test-Path -LiteralPath $FileName -PathType Leaf)) {
        Write-Output "!web.config not found: $FileName"
        $global:LASTEXITCODE = 2
        return
    }

    try { $commands = ConvertFrom-SettingCommands -Text $ConnectionStrings }
    catch {
        Write-Output "!Invalid ConnectionStrings: $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }

    $fullPath = (Resolve-Path -LiteralPath $FileName).ProviderPath
    $changed = $false

    try {
        $doc = New-Object System.Xml.XmlDocument
        $doc.PreserveWhitespace = $true
        $doc.Load($fullPath)

        $sections = @($doc.SelectNodes('//*') | Where-Object { $_.LocalName -ieq 'connectionStrings' })
        if ($sections.Count -ne 1) {
            throw "expected exactly one connectionStrings element but found $($sections.Count)"
        }
        $section = $sections[0]

        foreach ($cmd in $commands) {
            $existing = $null
            foreach ($node in $section.SelectNodes("*[local-name()='add']")) {
                if ($node.GetAttribute('name') -eq $cmd.Key) { $existing = $node; break }
            }
            if ($cmd.Command -eq 'delete') {
                if ($null -eq $existing) {
                    Write-Output ":Not deleted: no connection string named $($cmd.Key) exists."
                }
                else {
                    $old = $existing.GetAttribute('connectionString')
                    [void]$section.RemoveChild($existing)
                    $changed = $true
                    Write-Output ":Deleted connection string $($cmd.Key) (it was '$old')"
                }
            }
            elseif ($null -eq $existing) {
                $add = $doc.CreateElement('add', $section.NamespaceURI)
                $add.SetAttribute('name', $cmd.Key)
                $add.SetAttribute('connectionString', $cmd.Value)
                $add.SetAttribute('providerName', $ProviderName)
                [void]$section.AppendChild($add)
                $changed = $true
                Write-Output ":Created connection string $($cmd.Key) = $($cmd.Value) (provider $ProviderName)"
            }
            elseif ($existing.GetAttribute('connectionString') -eq $cmd.Value -and $existing.GetAttribute('providerName') -eq $ProviderName) {
                Write-Output ":Not updated: connection string $($cmd.Key) already has that value and provider."
            }
            else {
                $old = $existing.GetAttribute('connectionString')
                $oldProvider = $existing.GetAttribute('providerName')
                $existing.SetAttribute('connectionString', $cmd.Value)
                $existing.SetAttribute('providerName', $ProviderName)
                $changed = $true
                Write-Output ":Updated connection string $($cmd.Key). Was '$old' (provider '$oldProvider'), now '$($cmd.Value)' (provider '$ProviderName')"
            }
        }
    }
    catch {
        Write-Output "!Failed to read or update '$fullPath': $($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }

    if (-not $changed) {
        Write-Output ':No changes were needed; the file was not modified.'
        $global:LASTEXITCODE = 0
        return
    }

    try { Save-XmlDocument -Document $doc -Path $fullPath }
    catch {
        Write-Output "!$($_.Exception.Message)"
        $global:LASTEXITCODE = 1
        return
    }

    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Set-XmlValue, Set-WebConfigAppSettings, Set-WebConfigConnectionStrings
