@{
    RootModule           = 'Sbd.Wsl.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '3b8e1f52-6c7a-4d09-9a41-d27c05e8b6f3'
    Author               = 'Jay Allard'
    Description          = 'Windows Subsystem for Linux install and version checks for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Test-WslVersion', 'Stop-Wsl','Install-Wsl', 'Enable-VirtualMachinePlatform')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
