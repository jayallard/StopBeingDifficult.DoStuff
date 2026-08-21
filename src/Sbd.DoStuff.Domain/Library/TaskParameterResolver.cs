namespace Sbd.DoStuff.Domain.Library;

/// <summary>
/// Single place parameter precedence is decided: a Task List's supplied value always wins,
/// even over a value pinned by the definition's own inheritance chain — unless the
/// definition chain marked the parameter non-overridable, in which case supplying a value
/// throws rather than silently falling back to the pinned/default value.
/// </summary>
public static class TaskParameterResolver
{
    public static IReadOnlyDictionary<string, string> Resolve(
        EffectiveTaskDefinition effective, IReadOnlyDictionary<string, string>? suppliedValues)
    {
        var resolved = new Dictionary<string, string>();

        foreach (var parameter in effective.Parameters)
        {
            if (suppliedValues is not null && suppliedValues.TryGetValue(parameter.Name, out var supplied))
            {
                if (effective.NonOverridableParameterNames.Contains(parameter.Name))
                {
                    throw new ParameterNotOverridableException(effective.Id, parameter.Name);
                }

                resolved[parameter.Name] = supplied;
            }
            else if (effective.PinnedParameterValues.TryGetValue(parameter.Name, out var pinned))
            {
                resolved[parameter.Name] = pinned;
            }
            else if (parameter.DefaultValue is not null)
            {
                resolved[parameter.Name] = parameter.DefaultValue;
            }
            else if (parameter.Required)
            {
                throw new MissingRequiredParameterException(effective.Id, parameter.Name);
            }
            else
            {
                resolved[parameter.Name] = string.Empty;
            }
        }

        return resolved;
    }
}
