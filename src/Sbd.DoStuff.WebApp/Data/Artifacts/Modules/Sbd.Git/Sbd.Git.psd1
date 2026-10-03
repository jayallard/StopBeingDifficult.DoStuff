@{
    RootModule           = 'Sbd.Git.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '825824b6-87db-4138-828d-1b74eb0a2f8f'
    Author               = 'Jay Allard'
    Description          = 'Git install and version checks for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Install-Git', 'Test-GitVersion')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
