@{
    RootModule           = 'Sbd.Fnm.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '7c1e5b0a-3f52-4d0e-9a57-2b8d6e41c9f3'
    Author               = 'Jay Allard'
    Description          = 'fnm (Fast Node Manager) install and version checks for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Test-FnmVersion', 'Install-Fnm')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
