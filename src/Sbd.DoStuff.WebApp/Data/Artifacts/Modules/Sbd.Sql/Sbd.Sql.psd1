@{
    RootModule           = 'Sbd.Sql.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = 'e4a9d6b1-8c27-4f53-b0d9-61c3a7f2e85d'
    Author               = 'Jay Allard'
    Description          = 'Run SQL Server scripts in a single transaction for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Invoke-SqlScript')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
