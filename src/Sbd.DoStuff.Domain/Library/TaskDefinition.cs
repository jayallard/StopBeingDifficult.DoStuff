namespace Sbd.DoStuff.Domain.Library;

/// <summary>
/// Either a "base" definition (BaseTaskId is null; Type/Command carry the actual work) or a
/// "derived" definition (BaseTaskId is set; ParameterValues pins some of the base's
/// parameters; Type/Command/WorkingDirectory/EnvironmentVariables/Parameters/UseWindowsPowerShell
/// must all be null — enforced by YamlTaskLibrary at load time).
///
/// UseWindowsPowerShell forces execution via powershell.exe (Windows PowerShell 5.1) instead of
/// the default pwsh.exe (PowerShell 7), for tasks that depend on 5.1-only modules or behavior.
/// It has no effect on non-Windows platforms, which always run scripts via /bin/sh.
///
/// CanOverride lets any level of the BaseTaskId chain — base or derived — forbid a Task List
/// entry from overriding a parameter's resolved value, by mapping the parameter name to
/// false. It is inherited down the chain and is one-way: once a level sets a parameter to
/// false, no more-derived level can set it back to true (TaskDefinitionResolver treats false
/// as sticky regardless of chain order).
/// </summary>
public sealed record TaskDefinition(
    string Id,
    string Name,
    string? Description,
    string? BaseTaskId,
    IReadOnlyDictionary<string, string>? ParameterValues,
    string? Type,
    string? Command,
    string? ScriptPath,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string>? EnvironmentVariables,
    IReadOnlyList<TaskParameterDefinition>? Parameters,
    IReadOnlyDictionary<string, bool>? CanOverride = null,
    bool? UseWindowsPowerShell = null);
