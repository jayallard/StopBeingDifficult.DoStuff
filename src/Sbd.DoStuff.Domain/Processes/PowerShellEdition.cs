namespace Sbd.DoStuff.Domain.Processes;

/// <summary>Which PowerShell a shell task must run in. There is deliberately no fallback between them.</summary>
public enum PowerShellEdition
{
    /// <summary>PowerShell 7+ (<c>pwsh.exe</c>).</summary>
    Core,

    /// <summary>Windows PowerShell 5.1 (<c>powershell.exe</c>).</summary>
    Windows,
}

public static class PowerShellEditionNames
{
    /// <summary>
    /// Maps a task type name to its edition, ignoring case, spaces and hyphens, so the stored
    /// types ("powershell", "windows-powershell") and a selectList's display values
    /// ("PowerShell", "Windows PowerShell") are interchangeable. Null if it names neither.
    /// </summary>
    public static PowerShellEdition? FromTypeName(string? name) =>
        new string((name ?? string.Empty).Where(c => c is not (' ' or '-')).ToArray()).ToLowerInvariant() switch
        {
            "powershell" => PowerShellEdition.Core,
            "windowspowershell" => PowerShellEdition.Windows,
            _ => null,
        };
}
