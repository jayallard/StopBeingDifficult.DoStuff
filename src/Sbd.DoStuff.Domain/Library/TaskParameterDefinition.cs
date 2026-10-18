namespace Sbd.DoStuff.Domain.Library;

public sealed record TaskParameterDefinition(
    string Name,
    string? Description,
    bool Required,
    string? DefaultValue,
    bool CanOverride = true,
    // The value is the name of an application configuration key (e.g. "ConnectionStrings:Main"), not
    // the value itself; TaskFactory looks it up and hands the result to the task as an environment variable.
    bool ConfigurationSetting = false,
    TaskParameterType Type = TaskParameterType.Text,
    // The choices for a SelectList parameter, in display order.
    IReadOnlyList<string>? Options = null);
