@{
    RootModule           = 'Sbd.WinGet.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = 'b5581499-4c3d-4fea-8e76-decc63d56f15'
    Author               = 'Jay Allard'
    Description          = 'WinGet install and version checks for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Test-WinGetVersion', 'Install-WinGet')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
