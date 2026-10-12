namespace Sbd.DoStuff.Domain.Processes;

/// <summary>The PowerShell a task requires isn't installed (or isn't on PATH) on this machine.</summary>
public sealed class PowerShellNotFoundException(string message) : InvalidOperationException(message);
