namespace Sbd.DoStuff.Domain.Processes;

public sealed record ProcessRunResult(int ExitCode, bool WasCancelled);
