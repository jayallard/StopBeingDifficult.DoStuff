namespace Sbd.DoStuff.Domain.Library;

public static class TaskDefinitionResolver
{
    public static EffectiveTaskDefinition Resolve(TaskDefinition definition, ITaskLibrary library)
    {
        var chain = new List<string> { definition.Id };
        var pinned = new Dictionary<string, string>();
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

        return new EffectiveTaskDefinition(
            definition.Id,
            definition.Name,
            definition.Description,
            current.Type,
            current.Command,
            current.WorkingDirectory,
            current.EnvironmentVariables,
            current.Parameters ?? Array.Empty<TaskParameterDefinition>(),
            pinned);
    }
}
