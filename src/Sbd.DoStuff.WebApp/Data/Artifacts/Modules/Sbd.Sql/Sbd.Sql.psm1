# Functions report their outcome like a native command: they set $LASTEXITCODE (0 = success)
# rather than calling `exit`, so they're safe to call from an interactive session. Output lines
# starting with ':' are status messages and '!' are errors (the Sbd.DoStuff app's conventions).

# System.Data.SqlClient ships with both Windows PowerShell 5.1 and PowerShell 7.

function Update-ScanState {
    # Advances the lexical state across one line of T-SQL so that a `GO` inside a string literal,
    # quoted identifier or block comment isn't mistaken for a batch separator. State is one of
    # '' (code), "'" / '"' / ']' (inside that quoted token), or an integer depth for /* */ comments.
    param([string]$Line, $State)

    $i = 0
    while ($i -lt $Line.Length) {
        $c = $Line[$i]
        $next = if ($i + 1 -lt $Line.Length) { $Line[$i + 1] } else { [char]0 }

        if ($State -is [int]) {
            if ($c -eq '/' -and $next -eq '*') { $State++; $i += 2; continue }
            if ($c -eq '*' -and $next -eq '/') { $State--; $i += 2; if ($State -eq 0) { $State = '' }; continue }
            $i++; continue
        }

        if ($State -ne '') {
            # A doubled quote character is an escape; it never ends the token.
            if ($c -eq $State -and $next -eq $State) { $i += 2; continue }
            if ($c -eq $State) { $State = '' }
            $i++; continue
        }

        if ($c -eq '-' -and $next -eq '-') { break }
        if ($c -eq '/' -and $next -eq '*') { $State = 1; $i += 2; continue }
        if ($c -eq "'" -or $c -eq '"') { $State = [string]$c }
        elseif ($c -eq '[') { $State = ']' }
        $i++
    }
    return $State
}

function Split-SqlBatches {
    <#
    .SYNOPSIS
    Splits a script into batches on lines consisting solely of GO (optionally followed by a repeat count).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Script)

    $batches = New-Object System.Collections.Generic.List[string]
    $current = New-Object System.Text.StringBuilder
    $state = ''

    foreach ($line in ($Script -split "\r?\n")) {
        if ($state -eq '' -and $line -match '^\s*GO(?:\s+(\d+))?\s*(?:--.*)?$') {
            $repeat = if ($Matches[1]) { [int]$Matches[1] } else { 1 }
            $text = $current.ToString().Trim()
            if ($text) { 1..$repeat | ForEach-Object { $batches.Add($text) } }
            [void]$current.Clear()
            continue
        }
        [void]$current.AppendLine($line)
        $state = Update-ScanState -Line $line -State $state
    }

    $text = $current.ToString().Trim()
    if ($text) { $batches.Add($text) }
    return , $batches.ToArray()
}

function Format-SqlValue {
    param($Value)
    if ($null -eq $Value -or $Value -is [DBNull]) { return 'NULL' }
    if ($Value -is [byte[]]) { return '0x' + (($Value | ForEach-Object { $_.ToString('X2') }) -join '') }
    if ($Value -is [datetime]) { return $Value.ToString('yyyy-MM-dd HH:mm:ss.fff') }
    $text = [string]$Value
    $text = $text -replace '\s*[\r\n]+\s*', ' '
    if ($text.Length -gt 100) { $text = $text.Substring(0, 97) + '...' }
    return $text
}

function Format-SqlGrid {
    # Renders the reader's current result set as a monospace text grid and returns its lines.
    param([System.Data.SqlClient.SqlDataReader]$Reader)

    $names = @(for ($i = 0; $i -lt $Reader.FieldCount; $i++) {
        $name = $Reader.GetName($i)
        if ($name) { $name } else { '(No column name)' }
    })
    $numeric = @(for ($i = 0; $i -lt $Reader.FieldCount; $i++) {
        $Reader.GetFieldType($i) -in [byte], [int16], [int32], [int64], [decimal], [single], [double]
    })

    $rows = New-Object System.Collections.Generic.List[string[]]
    while ($Reader.Read()) {
        $cells = New-Object string[] $Reader.FieldCount
        for ($i = 0; $i -lt $Reader.FieldCount; $i++) { $cells[$i] = Format-SqlValue $Reader.GetValue($i) }
        $rows.Add($cells)
    }

    $widths = @(for ($i = 0; $i -lt $names.Count; $i++) {
        $max = $names[$i].Length
        foreach ($row in $rows) { if ($row[$i].Length -gt $max) { $max = $row[$i].Length } }
        $max
    })

    $format = {
        param([string[]]$Cells)
        $parts = for ($i = 0; $i -lt $Cells.Count; $i++) {
            if ($numeric[$i]) { $Cells[$i].PadLeft($widths[$i]) } else { $Cells[$i].PadRight($widths[$i]) }
        }
        ($parts -join ' | ').TrimEnd()
    }

    & $format $names
    ($widths | ForEach-Object { '-' * $_ }) -join '-+-'
    foreach ($row in $rows) { & $format $row }
    "($($rows.Count) row$(if ($rows.Count -ne 1) { 's' }))"
}

function Invoke-SqlScript {
    <#
    .SYNOPSIS
    Runs a SQL Server script as a single transaction, one batch at a time, on one connection.
    .DESCRIPTION
    The script is split into batches on GO lines (a script without GO is one batch). Each batch is
    echoed, then run; result sets are rendered as text grids, other batches report rows affected,
    and server messages (PRINT, RAISERROR, ...) are shown. Everything shares one session and
    transaction: if any batch fails the transaction is rolled back, otherwise it is committed.
    ConnectionStringSetting names the configuration key (e.g. ConnectionStrings:Main) whose value is the
    connection string; InitialCatalog, if given, overrides the database in it. The value is read from the environment variable of the same name (':' as '__').
    Sets $LASTEXITCODE to 0 on commit, or 1 if the script failed and was rolled back.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ConnectionStringSetting,
        [Parameter(Mandatory)][string]$Script,
        [string]$InitialCatalog,
        [int]$CommandTimeout = 300
    )

    $global:LASTEXITCODE = 1

    # The connection string is never a task parameter: the app resolves the named setting from its
    # configuration (appsettings, user secrets, environment, ...) and passes it as an environment
    # variable named like a .NET configuration key with ':' written as '__'.
    $variableName = $ConnectionStringSetting -replace ':', '__'
    $ConnectionString = [Environment]::GetEnvironmentVariable($variableName)
    if (-not $ConnectionString) {
        Write-Output "!No connection string found: set configuration setting '$ConnectionStringSetting' (environment variable '$variableName')."
        return
    }

    $batches = Split-SqlBatches -Script $Script
    if ($batches.Count -eq 0) {
        Write-Output ':The script contains no statements.'
        $global:LASTEXITCODE = 0
        return
    }

    $messages = New-Object System.Collections.Generic.List[string]
    if ($InitialCatalog) {
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
        $builder['Initial Catalog'] = $InitialCatalog
        $ConnectionString = $builder.ConnectionString
    }
    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    $transaction = $null
    $executed = 0
    try {
        $connection.add_InfoMessage({ param($sender, $e) $messages.Add($e.Message) })
        $connection.Open()
        $transaction = $connection.BeginTransaction()

        foreach ($batch in $batches) {
            Write-Output ''
            Write-Output $batch
            Write-Output ''

            $command = $connection.CreateCommand()
            $command.Transaction = $transaction
            $command.CommandText = $batch
            $command.CommandTimeout = $CommandTimeout

            $reader = $command.ExecuteReader()
            try {
                do {
                    if ($reader.FieldCount -gt 0) {
                        Format-SqlGrid -Reader $reader
                        Write-Output ''
                    }
                } while ($reader.NextResult())
                $affected = $reader.RecordsAffected
            }
            finally { $reader.Dispose() }

            foreach ($message in $messages) { Write-Output $message }
            $messages.Clear()
            if ($affected -ge 0) { Write-Output "($affected row$(if ($affected -ne 1) { 's' }) affected)" }
            $executed++
        }

        $transaction.Commit()
        $transaction = $null
        Write-Output ''
        Write-Output ":Committed $executed batch$(if ($executed -ne 1) { 'es' })."
        $global:LASTEXITCODE = 0
    }
    catch {
        foreach ($message in $messages) { Write-Output $message }
        $failure = if ($_.Exception.InnerException -is [System.Data.SqlClient.SqlException]) { $_.Exception.InnerException } else { $_.Exception }
        Write-Output "!$($failure.Message)"
        if ($failure -is [System.Data.SqlClient.SqlException]) {
            Write-Output "!(Msg $($failure.Number), Level $($failure.Class), State $($failure.State), Line $($failure.LineNumber))"
        }
        if ($transaction -and $transaction.Connection) {
            try { $transaction.Rollback(); Write-Output ':Rolled back.' }
            catch { Write-Output "!Rollback failed: $($_.Exception.Message)" }
        }
        $global:LASTEXITCODE = 1
    }
    finally {
        $connection.Dispose()
    }
}
