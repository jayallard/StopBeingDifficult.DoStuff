using Microsoft.Extensions.Configuration;
using Sbd.DoStuff.Domain.Tasks;

namespace Sbd.DoStuff.Domain.Library;

internal sealed class TaskFactory(IConfiguration? configuration = null) : ITaskFactory
{
    public ITask Create(EffectiveTaskDefinition effective, IReadOnlyDictionary<string, string> allParameterValues) =>
        effective.Type switch
        {
            "powershell" => CreateShellCommandTask(effective, allParameterValues),
            _ => throw new InvalidOperationException($"Unknown task type '{effective.Type}' for task '{effective.Id}'."),
        };

    private ShellCommandTask CreateShellCommandTask(
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
            kvp => kvp.Key, kvp => ParameterTemplate.Substitute(kvp.Value, values))
            ?? [];
        AddConfigurationSettings(effective, values, environmentVariables);

        return new ShellCommandTask(
            effective.Id, effective.Name, command, workingDirectory, environmentVariables, effective.Description,
            effective.UseWindowsPowerShell);
    }

    // A configuration-setting parameter holds the setting's *name*; the resolved value (which may be
    // a secret) goes to the child process only as an environment variable named after the setting
    // with ':' written as '__' (the .NET convention), never into the command text or run history.
    private void AddConfigurationSettings(
        EffectiveTaskDefinition effective, IReadOnlyDictionary<string, string> values,
        Dictionary<string, string> environmentVariables)
    {
        foreach (var parameter in effective.Parameters.Where(p => p.ConfigurationSetting))
        {
            if (!values.TryGetValue(parameter.Name, out var settingName) || settingName.Length == 0)
            {
                continue;
            }

            var variableName = settingName.Replace(":", "__");
            environmentVariables[variableName] = configuration?[settingName]
                ?? throw new InvalidOperationException(
                    $"Configuration setting '{settingName}' (parameter '{parameter.Name}' of task '{effective.Id}') is not set. " +
                    $"Set it as an environment variable ('{variableName}'), in appsettings.json, or in user secrets.");
        }
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
