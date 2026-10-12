@{
    RootModule           = 'Sbd.FileSystem.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '36edefc2-b588-4b83-baed-2575cb0c2f66'
    Author               = 'Jay Allard'
    Description          = 'File and folder management helpers (symbolic links, file copy, ...) for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('New-FolderLink', 'Copy-FileOverwrite')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
