namespace Sbd.DoStuff.Domain.Library;

/// <summary>
/// Either a "base" definition (BaseTaskId is null; Type/Command carry the actual work) or a
/// "derived" definition (BaseTaskId is set; ParameterValues pins some of the base's
/// parameters; Type/Command/WorkingDirectory/EnvironmentVariables/Parameters must all be
/// null — enforced by JsonTaskLibrary at load time).
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
    IReadOnlyList<TaskParameterDefinition>? Parameters);
