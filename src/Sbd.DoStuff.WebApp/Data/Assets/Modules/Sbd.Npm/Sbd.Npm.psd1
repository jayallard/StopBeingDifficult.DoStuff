@{
    RootModule           = 'Sbd.Npm.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = 'b3d9f6a2-61c4-4e8b-9d27-5a0e8c1f47d6'
    Author               = 'Jay Allard'
    Description          = 'Runs npm commands, optionally under an fnm-selected Node version, for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Invoke-NpmCommands')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
