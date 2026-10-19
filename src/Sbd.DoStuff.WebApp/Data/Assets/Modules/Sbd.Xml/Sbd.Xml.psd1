@{
    RootModule           = 'Sbd.Xml.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '7d2e9a64-1c3b-4f58-8a07-e6b4d1f93c25'
    Author               = 'Jay Allard'
    Description          = 'XML file manipulation for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Set-XmlValue', 'Set-WebConfigAppSettings', 'Set-WebConfigConnectionStrings')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
