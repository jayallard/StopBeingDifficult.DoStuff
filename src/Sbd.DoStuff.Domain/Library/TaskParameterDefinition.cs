namespace Sbd.DoStuff.Domain.Library;

public sealed record TaskParameterDefinition(
    string Name,
    string? Description,
    bool Required,
    string? DefaultValue,
    bool CanOverride = true);
