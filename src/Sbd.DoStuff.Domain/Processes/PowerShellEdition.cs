namespace Sbd.DoStuff.Domain.Processes;

/// <summary>Which PowerShell a shell task must run in. There is deliberately no fallback between them.</summary>
public enum PowerShellEdition
{
    /// <summary>PowerShell 7+ (<c>pwsh.exe</c>).</summary>
    Core,

    /// <summary>Windows PowerShell 5.1 (<c>powershell.exe</c>).</summary>
    Windows,
}
