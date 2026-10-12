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

Export-ModuleMember -Function Set-XmlValue
