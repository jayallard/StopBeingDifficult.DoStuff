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
