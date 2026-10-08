@{
    RootModule           = 'Sbd.Dotnet.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '4e8a2c71-95d3-4b6f-8a1e-0c7d3f92b5a8'
    Author               = 'Jay Allard'
    Description          = '.NET SDK listing and installation for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Get-DotnetSdks', 'Install-DotnetSdk')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
