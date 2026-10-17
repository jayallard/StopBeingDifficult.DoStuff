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

function Set-WebConfigAppSettings {
    <#
    .SYNOPSIS
    Adds or updates the entries of an appSettings or connectionStrings section in a web.config (or extension) file.
    .DESCRIPTION
    Settings is a multiline string of name=value pairs, one per line (split on the first '=', so values such as
    connection strings may contain '='; blank lines and lines starting with '#' are ignored; names and values are
    trimmed). SectionPath is a '/'-separated path of element names starting at the document's root element, e.g.
    'configuration/appSettings', 'appSettings' (an extension/configSource file whose root is the section) or
    'configuration/connectionStrings'. Names are matched case-insensitively and every element on the path must
    already exist. The <add> entries use key/value attributes, or name/connectionString when the section already
    holds connection strings (or is named connectionStrings); an existing providerName is left alone. The file's
    existing whitespace and declared encoding are preserved, and it is only rewritten if something changed.
    Sets $LASTEXITCODE to 0 on success, 1 on failure (bad input or unreadable/invalid XML), 2 if the file doesn't exist.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$Settings,
        [string]$SectionPath = 'configuration/appSettings'
    )

    if (-not (Test-Path -LiteralPath $FileName -PathType Leaf)) {
        Write-Output "!web.config not found: $FileName"
        $global:LASTEXITCODE = 2
        return
    }

    $segments = @($SectionPath.Trim().Trim('/') -split '/' | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
    if ($segments.Count -eq 0) {
        Write-Output '!SectionPath is empty.'
        $global:LASTEXITCODE = 1
        return
    }

    $pairs = @()
    foreach ($line in ($Settings -split "\r?\n")) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) {
            Write-Output "!Invalid setting line (expected name=value): $trimmed"
            $global:LASTEXITCODE = 1
            return
        }
        $pairs += [pscustomobject]@{ Key = $trimmed.Substring(0, $eq).Trim(); Value = $trimmed.Substring($eq + 1).Trim() }
    }
    if ($pairs.Count -eq 0) {
        Write-Output '!No settings supplied.'
        $global:LASTEXITCODE = 1
        return
    }

    $fullPath = (Resolve-Path -LiteralPath $FileName).ProviderPath
    $changed = $false

    try {
        $doc = New-Object System.Xml.XmlDocument
        $doc.PreserveWhitespace = $true
        $doc.Load($fullPath)

        $section = $doc.DocumentElement
        if ($null -eq $section -or $section.LocalName -ine $segments[0]) {
            $actualRoot = if ($null -eq $section) { '(none)' } else { $section.LocalName }
            throw "the root element is <$actualRoot> but SectionPath starts with '$($segments[0])'"
        }
        for ($i = 1; $i -lt $segments.Count; $i++) {
            $child = $null
            foreach ($candidate in $section.ChildNodes) {
                if ($candidate -is [System.Xml.XmlElement] -and $candidate.LocalName -ieq $segments[$i]) { $child = $candidate; break }
            }
            if ($null -eq $child) {
                throw "'$($segments[$i])' was not found under <$($section.LocalName)> (SectionPath '$SectionPath')"
            }
            $section = $child
        }

        # Entry shape: connectionStrings sections use name/connectionString; everything else (appSettings, or any
        # other element holding <add> children) uses key/value. An empty section is judged by its own name.
        $keyAttribute = 'key'; $valueAttribute = 'value'
        $firstAdd = $section.SelectSingleNode("*[local-name()='add']")
        if (($null -ne $firstAdd -and $firstAdd.HasAttribute('connectionString')) -or
            ($null -eq $firstAdd -and $section.LocalName -ieq 'connectionStrings')) {
            $keyAttribute = 'name'; $valueAttribute = 'connectionString'
        }

        foreach ($pair in $pairs) {
            $existing = $null
            foreach ($node in $section.SelectNodes("*[local-name()='add']")) {
                if ($node.GetAttribute($keyAttribute) -eq $pair.Key) { $existing = $node; break }
            }
            if ($null -eq $existing) {
                $add = $doc.CreateElement('add', $section.NamespaceURI)
                $add.SetAttribute($keyAttribute, $pair.Key)
                $add.SetAttribute($valueAttribute, $pair.Value)
                [void]$section.AppendChild($add)
                $changed = $true
                Write-Output ":Added the element setting $($pair.Key) = $($pair.Value)"
            }
            elseif ($existing.GetAttribute($valueAttribute) -eq $pair.Value) {
                Write-Output ":The element already exists with the key $($pair.Key) and value $($pair.Value). No change made."
            }
            else {
                $old = $existing.GetAttribute($valueAttribute)
                $existing.SetAttribute($valueAttribute, $pair.Value)
                $changed = $true
                Write-Output ":Element $($pair.Key) already exists. Changed value from $old to $($pair.Value)"
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

    try {
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

    $global:LASTEXITCODE = 0
}

Export-ModuleMember -Function Set-XmlValue, Set-WebConfigAppSettings
