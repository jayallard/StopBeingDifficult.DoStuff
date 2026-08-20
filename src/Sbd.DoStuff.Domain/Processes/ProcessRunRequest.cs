namespace Sbd.DoStuff.Domain.Processes;

public sealed record ProcessRunRequest(
    string Command,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string>? EnvironmentVariables);
