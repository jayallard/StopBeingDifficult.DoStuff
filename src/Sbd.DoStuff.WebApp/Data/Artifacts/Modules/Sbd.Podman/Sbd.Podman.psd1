@{
    RootModule           = 'Sbd.Podman.psm1'
    ModuleVersion        = '1.0.0'
    GUID                 = '8c3e1f5a-47d2-4b90-a6e8-2f9b7d4c1a63'
    Author               = 'Jay Allard'
    Description          = 'Podman and Compose install tasks for Sbd.DoStuff.'
    PowerShellVersion    = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport    = @('Test-PodmanVersion', 'Install-Podman', 'Uninstall-Podman', 'Install-PodmanCompose')
    CmdletsToExport      = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
