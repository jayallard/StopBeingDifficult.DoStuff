@{
    RootModule           = 'Sbd.Rancher.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '5d2a9c7e-81b4-4f36-b0e2-6a3f9d1c8e47'
    Author               = 'Jay Allard'
    Description          = 'Rancher Desktop install and version checks for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Test-RancherVersion', 'Install-Rancher')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
