using System.Diagnostics;
using Sbd.DoStuff.Domain.Execution;

namespace Sbd.DoStuff.Domain.Processes;

/// <param name="moduleDirectories">
/// Prepended to the child process's <c>PSModulePath</c>, so task commands can <c>Import-Module</c>
/// modules from them by name. A task's own <c>PSModulePath</c> environment variable still wins.
/// </param>
public abstract class ProcessRunnerBase(IEnumerable<string>? moduleDirectories = null) : IProcessRunner
{
    private readonly IReadOnlyList<string> _moduleDirectories = moduleDirectories?.ToList() ?? [];

    protected abstract (string FileName, string Arguments) BuildShellInvocation(string command, bool useWindowsPowerShell);

    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        Action<TaskOutputLine> onOutputLine,
        CancellationToken cancellationToken)
    {
        var (fileName, arguments) = BuildShellInvocation(request.Command, request.UseWindowsPowerShell);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (request.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        if (_moduleDirectories.Count > 0)
        {
            startInfo.Environment.TryGetValue("PSModulePath", out var existingModulePath);
            startInfo.Environment["PSModulePath"] = string.Join(
                Path.PathSeparator,
                _moduleDirectories.Append(existingModulePath).Where(path => !string.IsNullOrEmpty(path)));
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in request.EnvironmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutputLine(new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.StandardOutput, e.Data));
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutputLine(new TaskOutputLine(DateTimeOffset.UtcNow, OutputStream.StandardError, e.Data));
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessRunResult(process.ExitCode, WasCancelled: false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may have already exited in the gap between cancellation and the kill call.
            }

            return new ProcessRunResult(-1, WasCancelled: true);
        }
    }
}
