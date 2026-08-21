using Sbd.DoStuff.Domain.Tasks;

namespace Sbd.DoStuff.Domain.Library;

internal sealed class TaskFactory : ITaskFactory
{
    public ITask Create(EffectiveTaskDefinition effective, IReadOnlyDictionary<string, string> allParameterValues) =>
        effective.Type switch
        {
            "powershell" => CreateShellCommandTask(effective, allParameterValues),
            _ => throw new InvalidOperationException($"Unknown task type '{effective.Type}' for task '{effective.Id}'."),
        };

    private static ShellCommandTask CreateShellCommandTask(
        EffectiveTaskDefinition effective, IReadOnlyDictionary<string, string> values)
    {
        if (effective.Command is null)
        {
            throw new InvalidOperationException($"Shell task '{effective.Id}' has no Command.");
        }

        var command = ParameterTemplate.Substitute(effective.Command, values);
        var workingDirectory = effective.WorkingDirectory is null
            ? null
            : ParameterTemplate.Substitute(effective.WorkingDirectory, values);
        var environmentVariables = effective.EnvironmentVariables?.ToDictionary(
            kvp => kvp.Key, kvp => ParameterTemplate.Substitute(kvp.Value, values));

        return new ShellCommandTask(
            effective.Id, effective.Name, command, workingDirectory, environmentVariables, effective.Description);
    }
}
