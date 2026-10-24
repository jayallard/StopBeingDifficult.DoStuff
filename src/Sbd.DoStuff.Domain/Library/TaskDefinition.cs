namespace Sbd.DoStuff.Domain.Library;

/// <summary>
/// Either a "base" definition (BaseTaskId is null; Type/Command carry the actual work) or a
/// "derived" definition (BaseTaskId is set; ParameterValues pins some of the base's
/// parameters; Type/Command/WorkingDirectory/EnvironmentVariables/Parameters
/// must all be null — enforced by YamlTaskLibrary at load time).
///
/// Type "powershell" runs in PowerShell 7+ (pwsh.exe); "windows-powershell" runs in Windows
/// PowerShell 5.1 (powershell.exe), for tasks that depend on 5.1-only modules or behavior.
/// Neither falls back to the other: running a task whose shell isn't installed fails.
/// Type may reference a selectList parameter (e.g. "{Shell}" with options "PowerShell" and
/// "Windows PowerShell") to let the user pick the shell per run.
///
/// CanOverride lets any level of the BaseTaskId chain — base or derived — forbid a Task List
/// entry from overriding a parameter's resolved value, by mapping the parameter name to
/// false. It is inherited down the chain and is one-way: once a level sets a parameter to
/// false, no more-derived level can set it back to true (TaskDefinitionResolver treats false
/// as sticky regardless of chain order).
///
/// Icon names an image under the Icons asset folder (see TaskIconCatalog). A derived definition
/// that sets none inherits its base's icon; with none anywhere the library default is used.
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
    string? Icon = null);
