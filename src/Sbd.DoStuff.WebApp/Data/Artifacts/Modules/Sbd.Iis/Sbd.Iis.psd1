@{
    RootModule           = 'Sbd.Iis.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = 'c4cec5f4-d93b-4a59-80fe-625e26f3b4c8'
    Author               = 'Jay Allard'
    Description          = 'IIS install/update and version checks for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Install-Iis', 'Uninstall-Iis', 'Test-IisVersion')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
