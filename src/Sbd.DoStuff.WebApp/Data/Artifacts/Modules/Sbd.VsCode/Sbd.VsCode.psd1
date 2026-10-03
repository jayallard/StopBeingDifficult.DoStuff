@{
    RootModule           = 'Sbd.VsCode.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '2bce813f-dd60-41d4-9bb8-101fe2c5fa63'
    Author               = 'Jay Allard'
    Description          = 'VS Code checks for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Test-VsCodeInstalled')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
