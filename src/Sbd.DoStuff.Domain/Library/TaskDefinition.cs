namespace Sbd.DoStuff.Domain.Library;

/// <summary>
/// Either a "base" definition (BaseTaskId is null; Type/Command carry the actual work) or a
/// "derived" definition (BaseTaskId is set; ParameterValues pins some of the base's
/// parameters; Type/Command/WorkingDirectory/EnvironmentVariables/Parameters must all be
/// null — enforced by JsonTaskLibrary at load time).
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
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string>? EnvironmentVariables,
    IReadOnlyList<TaskParameterDefinition>? Parameters,
    IReadOnlyDictionary<string, bool>? CanOverride = null);
