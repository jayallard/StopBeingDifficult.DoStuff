namespace Sbd.DoStuff.Domain.Library;

public static class TaskDefinitionResolver
{
    public static EffectiveTaskDefinition Resolve(TaskDefinition definition, ITaskLibrary library)
    {
        var chain = new List<string> { definition.Id };
        var pinned = new Dictionary<string, string>();
        var nonOverridable = new HashSet<string>();
        var current = definition;

        while (current.BaseTaskId is not null)
        {
            if (current.ParameterValues is not null)
            {
                // TryAdd only: the leaf-most (most-derived) pin for a given name is
                // processed first, so it wins over the same name pinned further up the chain.
                foreach (var (key, value) in current.ParameterValues)
                {
                    pinned.TryAdd(key, value);
                }
            }

            AccumulateNonOverridable(current, nonOverridable);

            var baseDefinition = library.Find(current.BaseTaskId);
            if (baseDefinition is null || chain.Contains(baseDefinition.Id))
            {
                chain.Add(current.BaseTaskId);
                throw new TaskDefinitionCycleException(chain);
            }

            chain.Add(baseDefinition.Id);
            current = baseDefinition;
        }

        if (current.Type is null)
        {
            throw new InvalidOperationException($"Task definition '{current.Id}' has no Type.");
        }

        // The root never enters the loop above (it has no BaseTaskId), so its own CanOverride
        // map — and its parameters' declared CanOverride — are folded in here.
        AccumulateNonOverridable(current, nonOverridable);
        foreach (var parameter in current.Parameters ?? Array.Empty<TaskParameterDefinition>())
        {
            if (!parameter.CanOverride)
            {
                nonOverridable.Add(parameter.Name);
            }
        }

        return new EffectiveTaskDefinition(
            definition.Id,
            definition.Name,
            definition.Description,
            current.Type,
            current.Command,
            current.WorkingDirectory,
            current.EnvironmentVariables,
            current.Parameters ?? Array.Empty<TaskParameterDefinition>(),
            pinned,
            nonOverridable);
    }

    // A parameter locked to false anywhere in the chain stays locked: this only ever adds
    // names, never removes them, so a more-derived level setting CanOverride back to true
    // cannot undo a false set by a level closer to the root.
    private static void AccumulateNonOverridable(TaskDefinition definition, HashSet<string> nonOverridable)
    {
        if (definition.CanOverride is null)
        {
            return;
        }

        foreach (var (name, canOverride) in definition.CanOverride)
        {
            if (!canOverride)
            {
                nonOverridable.Add(name);
            }
        }
    }
}
