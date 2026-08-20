namespace Sbd.DoStuff.Domain.Library;

/// <summary>
/// The flattened result of walking a BaseTaskId chain. Parameters is the root's full
/// declared parameter list — nothing removed for pinned names, since a Task List entry
/// is allowed to override even an already-pinned value.
/// </summary>
public sealed record EffectiveTaskDefinition(
    string Id,
    string Name,
    string? Description,
    string Type,
    string? Command,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string>? EnvironmentVariables,
    IReadOnlyList<TaskParameterDefinition> Parameters,
    IReadOnlyDictionary<string, string> PinnedParameterValues);
