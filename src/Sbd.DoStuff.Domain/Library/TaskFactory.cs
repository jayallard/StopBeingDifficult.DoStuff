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
        var command = (effective.Command, effective.ScriptPath) switch
        {
            (not null, _) => PrependParameterAssignments(effective.Command, values),
            (null, not null) => BuildScriptFileInvocation(effective.ScriptPath, values),
            (null, null) => throw new InvalidOperationException(
                $"Shell task '{effective.Id}' has no Command or ScriptPath."),
        };
        var workingDirectory = effective.WorkingDirectory is null
            ? null
            : ParameterTemplate.Substitute(effective.WorkingDirectory, values);
        var environmentVariables = effective.EnvironmentVariables?.ToDictionary(
            kvp => kvp.Key, kvp => ParameterTemplate.Substitute(kvp.Value, values));

        return new ShellCommandTask(
            effective.Id, effective.Name, command, workingDirectory, environmentVariables, effective.Description);
    }

    private static string PrependParameterAssignments(string command, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            return command;
        }

        var assignments = values.Select(kvp => $"${kvp.Key} = {ToPowerShellStringLiteral(kvp.Value)}");
        return $"# --- Parameters ---\n{string.Join('\n', assignments)}\n# --- End Parameters ---\n{command}";
    }

    // Invoked with the call operator, not dot-sourced: the script gets its own scope and must
    // declare a matching `param(...)` block to receive these, since dot-sourcing would run the
    // script's own param block after these lines and clobber any values set beforehand.
    private static string BuildScriptFileInvocation(string scriptPath, IReadOnlyDictionary<string, string> values)
    {
        var arguments = values.Select(kvp => $"-{kvp.Key} {ToPowerShellStringLiteral(kvp.Value)}");
        var argumentList = values.Count == 0 ? string.Empty : " " + string.Join(' ', arguments);
        return $"& {ToPowerShellStringLiteral(scriptPath)}{argumentList}";
    }

    private static string ToPowerShellStringLiteral(string value) => $"'{value.Replace("'", "''")}'";
}
