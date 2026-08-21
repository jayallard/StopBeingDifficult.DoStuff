using Sbd.DoStuff.Domain.Processes;

namespace Sbd.DoStuff.Domain.Tasks;

public sealed class ShellCommandTask(
    string id,
    string name,
    string command,
    string? workingDirectory,
    IReadOnlyDictionary<string, string>? environmentVariables,
    string? description) : ITask
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string? Description { get; } = description;
    public string Command { get; } = command;
    public string? WorkingDirectory { get; } = workingDirectory;
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; } =
        environmentVariables ?? new Dictionary<string, string>();

    public async Task RunAsync(ITaskExecutionContext context, CancellationToken cancellationToken)
    {
        context.Log($"Running Powershell Script:");
        context.Log(string.Empty);
        context.Log(Command);

        var request = new ProcessRunRequest(Command, WorkingDirectory, EnvironmentVariables);
        var result = await context.ProcessRunner.RunAsync(request, context.Report, cancellationToken);

        context.SetResult(
            result.ExitCode,
            result.ExitCode == 0 ? "Completed successfully" : $"Process exited with code {result.ExitCode}");

        if (result.ExitCode != 0)
        {
            throw new TaskExecutionFailedException($"Process exited with code {result.ExitCode}.");
        }
    }
}
