@{
    RootModule           = 'Sbd.PowerShell.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '9534f3e5-2e95-402d-8f69-b90bd1638f05'
    Author               = 'Jay Allard'
    Description          = 'PowerShell 7+ install/update and version checks for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Install-PowerShell', 'Test-PowerShellVersion')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
