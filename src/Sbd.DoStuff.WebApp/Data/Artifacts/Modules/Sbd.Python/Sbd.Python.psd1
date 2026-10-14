@{
    RootModule           = 'Sbd.Python.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = 'e4a7c1d8-3f52-4b69-8a1e-6d90b2f73c15'
    Author               = 'Jay Allard'
    Description          = 'Installs and checks Python for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Install-Python', 'Test-PythonVersion')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
