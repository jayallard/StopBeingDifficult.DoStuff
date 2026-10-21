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

function Get-SqlConnectionString {
    # The connection string is never a task parameter: the app resolves the named setting from its
    # configuration (appsettings, user secrets, environment, ...) and passes it as an environment
    # variable named like a .NET configuration key with ':' written as '__'. Returns $null if it isn't
    # set (the caller reports that with Get-MissingConnectionStringMessage, so this function's output is
    # only ever the value).
    param([Parameter(Mandatory)][string]$Setting)

    [Environment]::GetEnvironmentVariable(($Setting -replace ':', '__'))
}

function Get-MissingConnectionStringMessage {
    param([Parameter(Mandatory)][string]$Setting)

    "!No connection string found: set configuration setting '$Setting' (environment variable '$($Setting -replace ':', '__')')."
}

function Invoke-SqlScript {
    <#
    .SYNOPSIS
    Runs a SQL Server script as a single transaction, one batch at a time, on one connection.
    .DESCRIPTION
    The script is split into batches on GO lines (a script without GO is one batch). Each batch is
    echoed, then run; result sets are rendered as text grids, other batches report rows affected,
    and server messages (PRINT, RAISERROR, ...) are shown. Everything shares one session and, by default,
    one transaction: if any batch fails the transaction is rolled back, otherwise it is committed.
    With -UseTransaction $false there is no transaction: each batch commits as it runs and the first
    failure stops the script, leaving earlier batches applied. That is needed for statements SQL Server
    won't run inside a transaction, such as ALTER DATABASE, RESTORE DATABASE and CREATE DATABASE.
    ConnectionStringSetting names the configuration key (e.g. ConnectionStrings:Main) whose value is the
    connection string; InitialCatalog, if given, overrides the database in it. The value is read from the environment variable of the same name (':' as '__').
    Sets $LASTEXITCODE to 0 on commit (or on completion without a transaction), or 1 if the script
    failed (and was rolled back, if it used a transaction).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ConnectionStringSetting,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Script,
        [string]$InitialCatalog,
        [int]$CommandTimeout = 300,
        [bool]$UseTransaction = $true
    )

    $global:LASTEXITCODE = 1

    $ConnectionString = Get-SqlConnectionString -Setting $ConnectionStringSetting
    if (-not $ConnectionString) {
        Write-Output (Get-MissingConnectionStringMessage -Setting $ConnectionStringSetting)
        return
    }

    $batches = Split-SqlBatches -Script $Script
    if ($batches.Count -eq 0) {
        Write-Output ':The script contains no statements.'
        $global:LASTEXITCODE = 0
        return
    }

    if ($InitialCatalog) {
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
        $builder['Initial Catalog'] = $InitialCatalog
        $ConnectionString = $builder.ConnectionString
    }
    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    $transaction = $null
    $executed = 0
    try {
        # Server messages are written straight to stdout the moment they arrive, not collected into the
        # pipeline: Write-Output inside an event handler doesn't reach the pipeline, and a long RESTORE
        # or a RAISERROR ... WITH NOWAIT progress line should be visible while the batch is still running.
        $connection.add_InfoMessage({
            param($sender, $e)
            [Console]::Out.WriteLine($e.Message)
            [Console]::Out.Flush()
        })
        $connection.Open()
        if ($UseTransaction) { $transaction = $connection.BeginTransaction() }
        else { Write-Output ':Running without a transaction: each batch is committed as it runs and is not rolled back if a later one fails.' }

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

            if ($affected -ge 0) { Write-Output "($affected row$(if ($affected -ne 1) { 's' }) affected)" }
            $executed++
        }

        Write-Output ''
        if ($transaction) {
            $transaction.Commit()
            $transaction = $null
            Write-Output ":Committed $executed batch$(if ($executed -ne 1) { 'es' })."
        }
        else { Write-Output ":Completed $executed batch$(if ($executed -ne 1) { 'es' }) (no transaction)." }
        $global:LASTEXITCODE = 0
    }
    catch {
        $failure = if ($_.Exception.InnerException -is [System.Data.SqlClient.SqlException]) { $_.Exception.InnerException } else { $_.Exception }
        Write-Output "!$($failure.Message)"
        if ($failure -is [System.Data.SqlClient.SqlException]) {
            Write-Output "!(Msg $($failure.Number), Level $($failure.Class), State $($failure.State), Line $($failure.LineNumber))"
        }
        if ($transaction -and $transaction.Connection) {
            try { $transaction.Rollback(); Write-Output ':Rolled back.' }
            catch { Write-Output "!Rollback failed: $($_.Exception.Message)" }
        }
        elseif (-not $UseTransaction -and $executed -gt 0) {
            Write-Output "!Stopped at batch $($executed + 1). $executed batch$(if ($executed -ne 1) { 'es' }) already ran without a transaction and cannot be rolled back."
        }
        $global:LASTEXITCODE = 1
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-SqlFile {
    <#
    .SYNOPSIS
    Runs a SQL script file exactly as Invoke-SqlScript runs a script given as text.
    .DESCRIPTION
    Fails (sets $LASTEXITCODE to 1) if the file doesn't exist. The file's encoding is detected from its
    byte-order mark, defaulting to UTF-8.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ConnectionStringSetting,
        [Parameter(Mandatory)][string]$ScriptPath,
        [string]$InitialCatalog,
        [int]$CommandTimeout = 300,
        [bool]$UseTransaction = $true
    )

    if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
        Write-Output "!SQL script file not found: $ScriptPath"
        $global:LASTEXITCODE = 1
        return
    }

    $fullPath = (Resolve-Path -LiteralPath $ScriptPath).ProviderPath
    Write-Output ":Script file: $fullPath"
    $script = [System.IO.File]::ReadAllText($fullPath)
    Invoke-SqlScript -ConnectionStringSetting $ConnectionStringSetting -Script $script `
        -InitialCatalog $InitialCatalog -CommandTimeout $CommandTimeout -UseTransaction $UseTransaction
}

$WinGetNoApplicableUpgrade = -1978335189

function Install-Ssms {
    <#
    .SYNOPSIS
    Installs SQL Server Management Studio via winget, if it isn't already installed.
    .DESCRIPTION
    Sets $LASTEXITCODE to winget's exit code, or 0 if SSMS is already installed and up to date.
    The installer may ask for administrator approval.
    .PARAMETER PackageId
    The winget package id; each major SSMS version has its own (e.g. Microsoft.SQLServerManagementStudio.21).
    #>
    [CmdletBinding()]
    param(
        [string]$PackageId = 'Microsoft.SQLServerManagementStudio.22'
    )

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Output "!WinGet is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    winget install --id $PackageId -e --source winget --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":SSMS ($PackageId) is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
}

function Install-SqlServerDeveloper {
    <#
    .SYNOPSIS
    Installs SQL Server Developer Edition via winget, if the default instance isn't already installed.
    .DESCRIPTION
    Defaults to SQL Server 2025: the SQL Server 2022 installer published by Microsoft (and in winget) is
    from 2022 and now refuses to run as "no longer supported". Sets $LASTEXITCODE to winget's exit code,
    or 0 if the default instance (MSSQLSERVER) already exists or the package is already up to date.
    The installer may ask for administrator approval.
    .PARAMETER PackageId
    The winget package id.
    .PARAMETER StartTimeoutSeconds
    How long to wait for the service to be running after the install; fails if it isn't by then.
    #>
    [CmdletBinding()]
    param(
        [string]$PackageId = 'Microsoft.SQLServer.2025.Developer',
        [int]$StartTimeoutSeconds = 600
    )

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Output "!WinGet is not installed."
        $global:LASTEXITCODE = 2
        return
    }

    if (Get-Service -Name MSSQLSERVER -ErrorAction SilentlyContinue) {
        Write-Output ':SQL Server default instance (MSSQLSERVER) is already installed'
        $global:LASTEXITCODE = 0
        return
    }

    winget install --id $PackageId -e --source winget --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -eq $WinGetNoApplicableUpgrade) {
        Write-Output ":SQL Server ($PackageId) is already installed and is up to date"
        $global:LASTEXITCODE = 0
    }
    if ($LASTEXITCODE -ne 0) { return }

    # The first start after an install can take minutes while the system databases are created.
    $deadline = (Get-Date).AddSeconds($StartTimeoutSeconds)
    $service = Get-Service -Name MSSQLSERVER -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Output '!Install finished but the SQL Server service (MSSQLSERVER) was not found.'
        $global:LASTEXITCODE = 1
        return
    }
    Write-Output ':Waiting for SQL Server to start'
    while ($service.Status -ne 'Running' -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        $service.Refresh()
    }
    if ($service.Status -eq 'Running') {
        Write-Output ':SQL Server is running'
    }
    else {
        Write-Output "!SQL Server did not start within $StartTimeoutSeconds seconds (service status: $($service.Status))."
        $global:LASTEXITCODE = 1
    }
}

function New-RandomSqlPassword {
    # Letters and digits only (so it needs no escaping in T-SQL or PowerShell), guaranteed to contain
    # upper case, lower case and a digit to satisfy the Windows password policy.
    param([int]$Length = 24)

    $upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ'; $lower = 'abcdefghijkmnopqrstuvwxyz'; $digits = '23456789'
    $all = $upper + $lower + $digits
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $pick = {
            param([string]$Chars)
            $bytes = New-Object byte[] 4
            $rng.GetBytes($bytes)
            $Chars[[int]([BitConverter]::ToUInt32($bytes, 0) % $Chars.Length)]
        }
        $chars = @((& $pick $upper), (& $pick $lower), (& $pick $digits))
        while ($chars.Count -lt $Length) { $chars += (& $pick $all) }
        # Shuffle so the guaranteed characters aren't always first.
        $order = $chars | ForEach-Object { $b = New-Object byte[] 4; $rng.GetBytes($b); [pscustomobject]@{ Key = [BitConverter]::ToUInt32($b, 0); Char = $_ } } | Sort-Object Key
        return -join ($order | ForEach-Object { $_.Char })
    }
    finally { $rng.Dispose() }
}

function Get-SqlServerLoginState {
    # Read-only: reports whether the instance accepts SQL Server logins and whether sa is enabled.
    param([Parameter(Mandatory)][string]$ConnectionString)

    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int) AS WindowsOnly,
       (SELECT CAST(is_disabled AS int) FROM sys.server_principals WHERE name = N'sa') AS SaDisabled
"@
        $reader = $command.ExecuteReader()
        try {
            [void]$reader.Read()
            $windowsOnly = $reader['WindowsOnly']
            $saDisabled = $reader['SaDisabled']
        }
        finally { $reader.Dispose() }
    }
    finally { $connection.Dispose() }

    [pscustomobject]@{
        MixedMode = ($windowsOnly -is [int] -and $windowsOnly -eq 0)
        SaEnabled = ($saDisabled -is [int] -and $saDisabled -eq 0)
    }
}

function Enable-SqlServerMixedMode {
    <#
    .SYNOPSIS
    Enables SQL Server authentication (mixed mode) on the default local instance and enables the sa login
    with a new random password, which is reported in the output. Does nothing if both are already enabled.
    .DESCRIPTION
    Connects with the given connection string (whose login must be a sysadmin, e.g. Windows authentication as
    the account that installed SQL Server). If the instance already accepts SQL Server logins and sa is
    enabled it reports success and changes nothing. Otherwise it sets the instance's LoginMode to mixed,
    enables sa and sets its password, then restarts the service (the login mode only takes effect after a
    restart; skipped when mixed mode was already on and only sa needed enabling), waits for it to be
    running and checks that sa can log in. Each change generates a new sa password. Sets $LASTEXITCODE to
    0 on success, 1 otherwise.
    .PARAMETER ConnectionStringSetting
    Name of the configuration setting holding the connection string (see Invoke-SqlScript). It must point
    at the local default instance, since the service restart is local.
    .PARAMETER StartTimeoutSeconds
    How long to wait for the service to restart and accept connections.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ConnectionStringSetting,
        [int]$StartTimeoutSeconds = 300
    )

    $global:LASTEXITCODE = 1

    $adminConnectionString = Get-SqlConnectionString -Setting $ConnectionStringSetting
    if (-not $adminConnectionString) {
        Write-Output (Get-MissingConnectionStringMessage -Setting $ConnectionStringSetting)
        return
    }
    try { $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $adminConnectionString }
    catch {
        Write-Output "!The connection string in '$ConnectionStringSetting' is not valid: $($_.Exception.Message)"
        return
    }

    # The restart below is of this machine's default instance, so the connection must target it.
    $server = ($builder.DataSource -replace '^(tcp|np|lpc):', '' -split ',')[0]
    if ($server -notmatch '^(\.|\(local\)|localhost|127\.0\.0\.1|::1|' + [regex]::Escape($env:COMPUTERNAME) + ')(\\MSSQLSERVER)?$') {
        Write-Output "!Connection string '$ConnectionStringSetting' targets '$($builder.DataSource)', but this task can only configure the local default instance."
        return
    }

    $service = Get-Service -Name MSSQLSERVER -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Output '!The SQL Server default instance (MSSQLSERVER) is not installed.'
        return
    }
    if ($service.Status -ne 'Running') {
        Write-Output "!The SQL Server service is not running (status: $($service.Status))."
        return
    }

    # Read-only check first: if mixed mode is already on and sa is usable there is nothing to do, and
    # in particular no restart and no new sa password.
    try { $state = Get-SqlServerLoginState -ConnectionString $adminConnectionString }
    catch {
        Write-Output "!Could not check the SQL Server login mode: $($_.Exception.Message)"
        return
    }
    if ($state.MixedMode -and $state.SaEnabled) {
        Write-Output ':Mixed mode (SQL Server and Windows Authentication) and the sa login are already enabled; nothing to do'
        $global:LASTEXITCODE = 0
        return
    }

    $password = New-RandomSqlPassword
    $connection = New-Object System.Data.SqlClient.SqlConnection $adminConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        # xp_instance_regwrite resolves the instance's registry path, whatever the SQL Server version.
        $loginModeStatement = if ($state.MixedMode) { '' } else {
            "EXEC xp_instance_regwrite N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'LoginMode', REG_DWORD, 2;"
        }
        $command.CommandText = @"
$loginModeStatement
ALTER LOGIN [sa] ENABLE;
ALTER LOGIN [sa] WITH PASSWORD = N'$password', CHECK_POLICY = OFF;
"@
        [void]$command.ExecuteNonQuery()
        if ($state.MixedMode) { Write-Output ':Enabled sa (mixed mode was already on)' }
        else { Write-Output ':Set login mode to SQL Server and Windows Authentication, and enabled sa' }
    }
    catch {
        Write-Output "!Could not configure SQL Server: $($_.Exception.Message)"
        return
    }
    finally { $connection.Dispose() }

    # The login mode only takes effect after a restart; enabling sa on its own does not need one.
    if (-not $state.MixedMode) {
        Write-Output ':Restarting SQL Server (required for the login mode to take effect)'
        try { Restart-Service -Name MSSQLSERVER -Force -ErrorAction Stop }
        catch {
            Write-Output ':Restart needs administrator approval'
            try {
                Start-Process -FilePath (Get-Process -Id $PID).Path -Verb RunAs -Wait -ErrorAction Stop `
                    -ArgumentList '-NoProfile', '-NonInteractive', '-Command', 'Restart-Service -Name MSSQLSERVER -Force'
            }
            catch {
                Write-Output "!Could not restart the SQL Server service: $($_.Exception.Message) Restart it manually to finish enabling mixed mode."
                return
            }
        }
    }

    # The service reports Running before it accepts connections, so verify by logging in as sa.
    $saBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $adminConnectionString
    $saBuilder['Integrated Security'] = $false
    $saBuilder['User ID'] = 'sa'
    $saBuilder['Password'] = $password
    $saBuilder['Connect Timeout'] = 5
    $sa = $saBuilder.ConnectionString
    $deadline = (Get-Date).AddSeconds($StartTimeoutSeconds)
    $loggedIn = $false
    while (-not $loggedIn -and (Get-Date) -lt $deadline) {
        $test = New-Object System.Data.SqlClient.SqlConnection $sa
        try { $test.Open(); $loggedIn = $true }
        catch { Start-Sleep -Seconds 5 }
        finally { $test.Dispose() }
    }
    if (-not $loggedIn) {
        Write-Output "!SQL Server did not accept an sa login within $StartTimeoutSeconds seconds of the restart."
        return
    }

    Write-Output ':SQL Server is running and the sa login works'
    Write-Output ":sa password: $password"
    $global:LASTEXITCODE = 0
}

function Get-SqlDatabaseSize {
    <#
    .SYNOPSIS
    Lists every database on the instance with the disk space its data and log files take.
    .DESCRIPTION
    Sizes are the files' current allocated size from sys.master_files (not the space used inside them),
    in MB, largest first, followed by the total across all databases. Output is rendered by
    Invoke-SqlScript. Sets $LASTEXITCODE to 0 on success, 1 otherwise.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ConnectionStringSetting,
        [int]$CommandTimeout = 300
    )

    $query = @'
SELECT d.name AS [Database],
       d.state_desc AS [State],
       CAST(SUM(CASE WHEN f.type = 0 THEN f.size ELSE 0 END) * 8.0 / 1024 AS decimal(18, 1)) AS [Data MB],
       CAST(SUM(CASE WHEN f.type = 1 THEN f.size ELSE 0 END) * 8.0 / 1024 AS decimal(18, 1)) AS [Log MB],
       CAST(SUM(f.size) * 8.0 / 1024 AS decimal(18, 1)) AS [Total MB]
FROM sys.databases AS d
LEFT JOIN sys.master_files AS f ON f.database_id = d.database_id
GROUP BY d.name, d.state_desc
ORDER BY [Total MB] DESC, d.name;

SELECT COUNT(DISTINCT database_id) AS [Databases],
       CAST(SUM(size) * 8.0 / 1024 AS decimal(18, 1)) AS [All databases MB]
FROM sys.master_files;
'@

    Invoke-SqlScript -ConnectionStringSetting $ConnectionStringSetting -Script $query -CommandTimeout $CommandTimeout
}

function Remove-SqlUser {
    <#
    .SYNOPSIS
    Drops database users, and server logins with the same names, from a SQL Server instance.
    .DESCRIPTION
    For each name in -UserNames, drops the matching user (SQL user, Windows user or Windows group) from
    each database in -DatabaseNames, then drops the server login of the same name if there is one.
    Names and databases may be separated by commas, semicolons or new lines. Before a user is dropped,
    any schema it owns is handed to dbo (the schema itself is kept), since a user that owns a schema
    can't be dropped. Before a login is dropped, its open sessions are killed. 'sa' and the login
    this task connects with are never dropped, and databases that are missing, offline or read-only
    are reported and skipped.
    Reports on every user in every database and on every login: dropped (or, in a dry run, would be
    dropped), not done and why (no such user, database not found, built-in principal, ...), or failed
    with the server's error. Each statement is run on its own so a failure does not stop the rest. By default
    this is a dry run: nothing is changed, the report just says what would be done. Pass -DryRun $false to run them.
    Users in databases that are not listed are left behind as orphans, and a login that owns a
    database or SQL Agent jobs can't be dropped until those are reassigned.
    Sets $LASTEXITCODE to 0 if everything succeeded, or 1 if any statement failed or any listed
    database was skipped.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ConnectionStringSetting,
        [Parameter(Mandatory)][string[]]$UserNames,
        [Parameter(Mandatory)][string[]]$DatabaseNames,
        [bool]$DryRun = $true,
        [int]$CommandTimeout = 300
    )

    $global:LASTEXITCODE = 1

    $split = {
        param($values)
        @($values | ForEach-Object { $_ -split '[,;\r\n]+' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
    }
    $literals = {
        param($values)
        ($values | ForEach-Object { "(N'" + ($_ -replace "'", "''") + "')" }) -join ', '
    }

    $users = & $split $UserNames
    $databases = & $split $DatabaseNames
    if ($users.Count -eq 0) { Write-Output '!No user names were given.'; return }
    if ($databases.Count -eq 0) { Write-Output '!No database names were given.'; return }

    $template = @'
SET NOCOUNT ON;

DECLARE @DryRun bit = {{DRYRUN}};
DECLARE @Db sysname, @Target sysname, @Stmt nvarchar(max), @Skip nvarchar(400), @Problem bit,
        @Sessions int, @Exec nvarchar(300), @Subject nvarchar(400), @Note nvarchar(200);
DECLARE @Done int = 0, @NotDone int = 0, @Failed int = 0;

CREATE TABLE #Targets (Name sysname NOT NULL PRIMARY KEY);
INSERT INTO #Targets (Name) VALUES {{USERS}};

CREATE TABLE #Databases (Name sysname NOT NULL PRIMARY KEY);
INSERT INTO #Databases (Name) VALUES {{DATABASES}};

-- One row per user per database, plus one per login (DbName NULL). A row either has a statement to
-- run, or a reason it can't be done (Problem = 1 if that is something the caller should look at,
-- 0 if it is just that the user or login doesn't exist).
CREATE TABLE #Plan (
    Id int IDENTITY(1, 1) PRIMARY KEY,
    Target sysname NOT NULL,
    DbName sysname NULL,
    Stmt nvarchar(max) NULL,
    Sessions int NOT NULL DEFAULT 0,
    Skip nvarchar(400) NULL,
    Problem bit NOT NULL DEFAULT 0);

IF @DryRun = 1 PRINT N':DRY RUN - nothing is changed. Run again with DryRun set to false to apply.';

-- Users: every target in every listed database
INSERT INTO #Plan (Target, DbName, Skip, Problem)
SELECT t.Name,
       ISNULL(sd.name, d.Name),
       CASE WHEN sd.database_id IS NULL THEN N'the database was not found'
            WHEN sd.state_desc <> N'ONLINE' THEN N'the database is ' + LOWER(sd.state_desc)
            WHEN sd.is_read_only = 1 THEN N'the database is read-only' END,
       CASE WHEN sd.database_id IS NULL OR sd.state_desc <> N'ONLINE' OR sd.is_read_only = 1 THEN 1 ELSE 0 END
FROM #Targets AS t
CROSS JOIN #Databases AS d
LEFT JOIN sys.databases AS sd ON sd.name = d.Name COLLATE DATABASE_DEFAULT;

-- The probe runs inside each usable database, so names are compared using that database's collation.
DECLARE @Probe nvarchar(max) = N'
UPDATE p
SET Skip = CASE WHEN dp.principal_id IS NULL THEN N''no user with that name exists in the database''
                WHEN dp.principal_id <= 4 THEN N''it is a built-in principal, which is never dropped''
                WHEN dp.type NOT IN (N''S'', N''U'', N''G'')
                     THEN N''it is a '' + LOWER(REPLACE(dp.type_desc, N''_'', N'' '')) + N'', not a user'' END
FROM #Plan AS p
LEFT JOIN sys.database_principals AS dp ON dp.name = p.Target COLLATE DATABASE_DEFAULT
WHERE p.DbName = DB_NAME() AND p.Skip IS NULL;

UPDATE p
SET Problem = 1
FROM #Plan AS p
JOIN sys.database_principals AS dp ON dp.name = p.Target COLLATE DATABASE_DEFAULT
WHERE p.DbName = DB_NAME() AND p.Skip IS NOT NULL;

UPDATE p
SET Stmt = ISNULL((SELECT N''ALTER AUTHORIZATION ON SCHEMA::'' + QUOTENAME(s.name) + N'' TO dbo; ''
                   FROM sys.schemas AS s
                   WHERE s.principal_id = dp.principal_id
                   FOR XML PATH(N''''), TYPE).value(N''.'', N''nvarchar(max)''), N'''')
           + N''DROP USER '' + QUOTENAME(dp.name) + N'';''
FROM #Plan AS p
JOIN sys.database_principals AS dp ON dp.name = p.Target COLLATE DATABASE_DEFAULT
WHERE p.DbName = DB_NAME() AND p.Skip IS NULL;';

DECLARE db_cursor CURSOR LOCAL STATIC FORWARD_ONLY READ_ONLY FOR
    SELECT DISTINCT DbName FROM #Plan WHERE DbName IS NOT NULL AND Skip IS NULL ORDER BY DbName;
OPEN db_cursor;
FETCH NEXT FROM db_cursor INTO @Db;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Exec = QUOTENAME(@Db) + N'.sys.sp_executesql';
    BEGIN TRY
        EXEC @Exec @Probe;
    END TRY
    BEGIN CATCH
        UPDATE #Plan
        SET Skip = N'the database could not be inspected: ' + LEFT(ERROR_MESSAGE(), 300), Problem = 1
        WHERE DbName = @Db AND Skip IS NULL AND Stmt IS NULL;
    END CATCH;
    FETCH NEXT FROM db_cursor INTO @Db;
END;
CLOSE db_cursor;
DEALLOCATE db_cursor;

-- Logins: one row per target
INSERT INTO #Plan (Target, DbName) SELECT Name, NULL FROM #Targets;

UPDATE p
SET Skip = CASE WHEN sp.principal_id IS NULL THEN N'no server login with that name exists'
                WHEN sp.principal_id = 1 THEN N'sa is never dropped'
                WHEN sp.name = SUSER_SNAME() THEN N'it is the login this task connects with'
                WHEN sp.name LIKE N'##%' THEN N'it is a system-created login, which is never dropped'
                WHEN sp.type NOT IN (N'S', N'U', N'G')
                     THEN N'it is a ' + LOWER(REPLACE(sp.type_desc, N'_', N' ')) + N', not a login' END
FROM #Plan AS p
LEFT JOIN sys.server_principals AS sp ON sp.name = p.Target COLLATE DATABASE_DEFAULT
WHERE p.DbName IS NULL;

UPDATE p
SET Problem = 1
FROM #Plan AS p
JOIN sys.server_principals AS sp ON sp.name = p.Target COLLATE DATABASE_DEFAULT
WHERE p.DbName IS NULL AND p.Skip IS NOT NULL;

UPDATE p
SET Sessions = (SELECT COUNT(*) FROM sys.dm_exec_sessions AS es
                WHERE es.login_name = sp.name AND es.session_id <> @@SPID AND es.is_user_process = 1),
    Stmt = ISNULL((SELECT N'BEGIN TRY KILL ' + CAST(es.session_id AS nvarchar(10)) + N'; END TRY BEGIN CATCH END CATCH; '
                   FROM sys.dm_exec_sessions AS es
                   WHERE es.login_name = sp.name AND es.session_id <> @@SPID AND es.is_user_process = 1
                   FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'), N'')
           + N'DROP LOGIN ' + QUOTENAME(sp.name) + N';'
FROM #Plan AS p
JOIN sys.server_principals AS sp ON sp.name = p.Target COLLATE DATABASE_DEFAULT
WHERE p.DbName IS NULL AND p.Skip IS NULL;

-- Report on (and, unless it is a dry run, carry out) each row. For a given name the users come
-- before the login.
DECLARE run_cursor CURSOR LOCAL STATIC FORWARD_ONLY READ_ONLY FOR
    SELECT Target, DbName, Stmt, Sessions, Skip, Problem
    FROM #Plan
    ORDER BY Target, CASE WHEN DbName IS NULL THEN 1 ELSE 0 END, DbName;
OPEN run_cursor;
FETCH NEXT FROM run_cursor INTO @Target, @Db, @Stmt, @Sessions, @Skip, @Problem;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Subject = ISNULL(N'[' + @Db + N'] user ', N'[server] login ') + QUOTENAME(@Target);
    SET @Note = CASE WHEN @Db IS NULL AND @Sessions > 0
                          THEN N' (' + CASE WHEN @DryRun = 1 THEN N'would kill ' ELSE N'killed ' END
                               + CAST(@Sessions AS nvarchar(10)) + N' open session(s))'
                     WHEN @Db IS NOT NULL AND @Stmt LIKE N'ALTER AUTHORIZATION%'
                          THEN N' (its schemas ' + CASE WHEN @DryRun = 1 THEN N'would be moved' ELSE N'were moved' END + N' to dbo)'
                     ELSE N'' END;

    IF @Skip IS NOT NULL
    BEGIN
        IF @Problem = 1
        BEGIN
            PRINT N'!' + @Subject + N': NOT DONE - ' + @Skip;
            SET @Failed += 1;
        END
        ELSE
        BEGIN
            PRINT N':' + @Subject + N': not done - ' + @Skip;
            SET @NotDone += 1;
        END;
    END
    ELSE IF @DryRun = 1
    BEGIN
        PRINT N':' + @Subject + N': would be dropped' + @Note;
        SET @Done += 1;
    END
    ELSE
    BEGIN
        SET @Exec = ISNULL(QUOTENAME(@Db) + N'.', N'') + N'sys.sp_executesql';
        BEGIN TRY
            EXEC @Exec @Stmt;
            PRINT N':' + @Subject + N': dropped' + @Note;
            SET @Done += 1;
        END TRY
        BEGIN CATCH
            PRINT N'!' + @Subject + N': FAILED - ' + ERROR_MESSAGE();
            SET @Failed += 1;
        END CATCH;
    END;

    FETCH NEXT FROM run_cursor INTO @Target, @Db, @Stmt, @Sessions, @Skip, @Problem;
END;
CLOSE run_cursor;
DEALLOCATE run_cursor;

PRINT N':Summary: ' + CAST(@Done AS nvarchar(10)) + CASE WHEN @DryRun = 1 THEN N' would be dropped, ' ELSE N' dropped, ' END
      + CAST(@NotDone AS nvarchar(10)) + N' not done (nothing to drop), '
      + CAST(@Failed AS nvarchar(10)) + N' failed or blocked.';

IF @Failed > 0
    RAISERROR(N'Some users or logins could not be dropped; see the lines starting with ! above.', 16, 1);
'@

    $script = $template.
        Replace('{{DRYRUN}}', $(if ($DryRun) { '1' } else { '0' })).
        Replace('{{USERS}}', (& $literals $users)).
        Replace('{{DATABASES}}', (& $literals $databases))

    # Each statement is run separately and its failure is caught in T-SQL, so no transaction: a doomed
    # transaction would stop the statements after the first failure.
    Invoke-SqlScript -ConnectionStringSetting $ConnectionStringSetting -Script $script -CommandTimeout $CommandTimeout -UseTransaction $false
}

function Find-SqlPackage {
    # Returns the path to sqlpackage, or $null. Looks on PATH (re-reading it from the registry first, in
    # case it was just installed), then in the usual install locations: the `dotnet tool` folder, the
    # standalone DAC framework folder, and Visual Studio's bundled copy. The newest match wins.
    $command = Get-Command sqlpackage -ErrorAction SilentlyContinue
    if (-not $command) {
        $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
        $user = [Environment]::GetEnvironmentVariable('Path', 'User')
        $env:Path = (@($machine, $user, $env:Path) | Where-Object { $_ }) -join ';'
        $command = Get-Command sqlpackage -ErrorAction SilentlyContinue
    }
    if ($command) { return $command.Source }

    $patterns = @(
        (Join-Path $env:USERPROFILE '.dotnet\tools\sqlpackage.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft SQL Server\*\DAC\bin\sqlpackage.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\*\*\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\sqlpackage.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft SQL Server\*\DAC\bin\sqlpackage.exe')
    )
    $found = foreach ($pattern in $patterns) {
        Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending
    }
    $first = $found | Select-Object -First 1
    if ($first) { return $first.FullName }
    $null
}

function Publish-DacPac {
    <#
    .SYNOPSIS
    Publishes a .dacpac to a SQL Server database with SqlPackage, using a publish profile (.publish.xml).
    .DESCRIPTION
    Runs `sqlpackage /Action:Publish` with the .dacpac as the source and the publish profile for the
    deployment options (drop objects not in source, block on data loss, SQLCMD variables, ...). If
    ConnectionStringSetting is given, the server and login come from the connection string it names (read from
    the environment variable of the same name, ':' as '__') and the profile's own TargetConnectionString is
    ignored. The database is the profile's TargetDatabaseName, or TargetDatabaseName if given (which overrides
    it), or the connection string's own database if neither names one. (SqlPackage rejects a connection string
    combined with other target arguments such as /TargetTimeout or /TargetDatabaseName, so the module passes the
    timeout as the CommandTimeout deployment property and puts the database into the connection string.) If
    ConnectionStringSetting is omitted, the target is whatever the profile names (so it must hold a working
    connection string), and TargetDatabaseName, if given, overrides the profile's database. SqlCmdVariables
    are NAME=VALUE entries separated by commas or new lines and override the profile's variable values.
    With -ScriptOnly nothing is changed: the deployment script is generated and printed instead.
    Sets $LASTEXITCODE to SqlPackage's exit code (0 = success), or 1 if it couldn't be started.
    #>
    [CmdletBinding()]
    param(
        [string]$ConnectionStringSetting,
        [Parameter(Mandatory)][string]$DacPacPath,
        [Parameter(Mandatory)][string]$PublishProfilePath,
        [string]$TargetDatabaseName,
        [string]$SqlCmdVariables,
        [int]$CommandTimeout = 300,
        [bool]$ScriptOnly = $false
    )

    $global:LASTEXITCODE = 1

    $connectionString = $null
    if ($ConnectionStringSetting) {
        $connectionString = Get-SqlConnectionString -Setting $ConnectionStringSetting
        if (-not $connectionString) {
            Write-Output (Get-MissingConnectionStringMessage -Setting $ConnectionStringSetting)
            return
        }
    }

    foreach ($file in @(@('DacPac', $DacPacPath), @('Publish profile', $PublishProfilePath))) {
        if (-not (Test-Path -LiteralPath $file[1] -PathType Leaf)) {
            Write-Output "!$($file[0]) file not found: $($file[1])"
            return
        }
    }
    $dacPac = (Resolve-Path -LiteralPath $DacPacPath).ProviderPath
    $profilePath = (Resolve-Path -LiteralPath $PublishProfilePath).ProviderPath

    $sqlPackage = Find-SqlPackage
    if (-not $sqlPackage) {
        Write-Output '!SqlPackage was not found. Install it with: dotnet tool install -g microsoft.sqlpackage (or winget install Microsoft.SqlPackage).'
        return
    }

    $variables = @($SqlCmdVariables -split '[,\r\n]+' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    foreach ($variable in $variables) {
        if ($variable -notmatch '^[^=\s]+=') {
            Write-Output "!Invalid SQLCMD variable '$variable': expected NAME=VALUE."
            return
        }
    }

    # With /TargetConnectionString on the command line, SqlPackage still takes the profile's deployment
    # options and (when the connection string has no database of its own) its TargetDatabaseName, but it
    # rejects any other target argument, /TargetTimeout and /TargetDatabaseName included. So the timeout
    # goes in as the CommandTimeout deployment property, and the database goes into the connection string:
    # the TargetDatabaseName parameter if given, else the profile's TargetDatabaseName (so a database in the
    # configured connection string doesn't win over the one the profile intends).
    $databaseName = $TargetDatabaseName
    if ($connectionString) {
        try {
            if (-not $databaseName) {
                $xml = New-Object System.Xml.XmlDocument
                $xml.Load($profilePath)
                $profileDatabase = $xml.SelectSingleNode("//*[local-name()='TargetDatabaseName']")
                if ($profileDatabase -and $profileDatabase.InnerText.Trim()) { $databaseName = $profileDatabase.InnerText.Trim() }
            }
            if ($databaseName) {
                $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connectionString
                $builder['Initial Catalog'] = $databaseName
                $connectionString = $builder.ConnectionString
            }
        }
        catch {
            Write-Output "!Could not prepare the connection string or read publish profile '$profilePath': $($_.Exception.Message)"
            return
        }
    }

    $action = if ($ScriptOnly) { 'Script' } else { 'Publish' }
    $arguments = @(
        "/Action:$action",
        "/SourceFile:$dacPac",
        "/Profile:$profilePath",
        "/p:CommandTimeout=$CommandTimeout"
    )
    if ($connectionString) { $arguments += "/TargetConnectionString:$connectionString" }
    elseif ($TargetDatabaseName) { $arguments += "/TargetDatabaseName:$TargetDatabaseName" }
    foreach ($variable in $variables) { $arguments += "/Variables:$variable" }

    $scriptFile = $null
    if ($ScriptOnly) {
        $scriptFile = Join-Path ([System.IO.Path]::GetTempPath()) "dacpac-$([guid]::NewGuid().ToString('N')).sql"
        $arguments += "/OutputPath:$scriptFile"
    }

    Write-Output ":SqlPackage: $sqlPackage"
    Write-Output ":DacPac: $dacPac"
    Write-Output ":Publish profile: $profilePath"
    if ($connectionString) { Write-Output ':Server and login come from the configured connection string; the publish profile''s own connection string is ignored.' }
    else { Write-Output ':No connection string setting given; the target comes from the publish profile.' }
    if ($databaseName) { Write-Output ":Target database: $databaseName$(if ($TargetDatabaseName) { ' (override)' } elseif ($connectionString) { ' (from the publish profile)' })" }
    foreach ($variable in $variables) { Write-Output ":SQLCMD variable: $variable" }
    if ($ScriptOnly) { Write-Output ':Script only: generating the deployment script, nothing will be changed.' }

    # The connection string is deliberately not echoed (it may hold a password).
    & $sqlPackage @arguments 2>&1 | ForEach-Object { "$_" }
    $exitCode = $LASTEXITCODE

    if ($scriptFile -and (Test-Path -LiteralPath $scriptFile)) {
        Write-Output ''
        Write-Output ':Deployment script:'
        Get-Content -LiteralPath $scriptFile
        Remove-Item -LiteralPath $scriptFile -Force -ErrorAction SilentlyContinue
    }

    if ($exitCode -eq 0) {
        Write-Output $(if ($ScriptOnly) { ':Deployment script generated.' } else { ':DacPac published.' })
    }
    else {
        Write-Output "!SqlPackage failed with exit code $exitCode."
    }
    $global:LASTEXITCODE = $exitCode
}
