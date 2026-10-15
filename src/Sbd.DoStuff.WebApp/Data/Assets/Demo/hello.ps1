param([Parameter(ValueFromRemainingArguments)][string[]]$Names)

if (-not $Names) { $Names = @('world') }
foreach ($name in $Names) {
    Write-Output ":Hello, $name!"
}
