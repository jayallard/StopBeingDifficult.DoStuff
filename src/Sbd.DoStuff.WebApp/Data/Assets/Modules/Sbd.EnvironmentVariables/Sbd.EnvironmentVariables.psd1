@{
    RootModule           = 'Sbd.EnvironmentVariables.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = 'b3f1c7a2-5d84-4e0a-9c6b-2a7e41d95f30'
    Author               = 'Jay Allard'
    Description          = 'Persistent environment variable management (Windows only) for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Set-EnvironmentVariable', 'Get-EnvironmentVariable')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
