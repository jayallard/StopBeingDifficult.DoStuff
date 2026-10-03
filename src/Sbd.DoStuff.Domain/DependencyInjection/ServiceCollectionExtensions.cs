using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.Domain.Processes;
using Sbd.DoStuff.Domain.Validation;

namespace Sbd.DoStuff.Domain.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDoStuffDomain(this IServiceCollection services, IConfiguration configuration)
    {
        // Each artifacts directory's Modules folder goes on PSModulePath, so task commands can
        // `Import-Module <Name>` the same modules a user can import directly in PowerShell.
        var moduleDirectories = RequireDirectories(configuration, "Artifacts:Directories")
            .Select(directory => Path.GetFullPath(Path.Combine(ExpandHomeDirectory(directory), "Modules")))
            .ToArray();
        services.AddSingleton<IProcessRunner>(_ => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsProcessRunner(moduleDirectories)
            : new UnixProcessRunner(moduleDirectories));

        services.AddSingleton<ITaskFactory, Library.TaskFactory>();

        services.AddSingleton<ITaskLibrary>(provider => new YamlTaskLibrary(
            RequireDirectories(configuration, "TaskLibrary:Directories").Select(ExpandHomeDirectory),
            RequireDirectories(configuration, "Artifacts:Directories").Select(ExpandHomeDirectory),
            provider.GetRequiredService<ILogger<YamlTaskLibrary>>()));
        services.AddSingleton<ITaskListRepository>(provider =>
        {
            var directories = RequireDirectories(configuration, "TaskLists:Directories").Select(ExpandHomeDirectory).ToArray();
            var storage = new TaskListStorageOptions(
                PublicDirectory: ExpandHomeDirectory(configuration["TaskLists:PublicDirectory"] ?? directories[0]),
                PrivateDirectory: ExpandHomeDirectory(configuration["TaskLists:PrivateDirectory"] ?? "~/.sbd.dostuff/TaskLists"));

            // Make sure lists saved to the write directories are also loaded on the next start.
            var loadDirectories = directories
                .Concat([storage.PublicDirectory, storage.PrivateDirectory])
                .DistinctBy(d => Path.GetFullPath(d), StringComparer.OrdinalIgnoreCase);

            return new YamlTaskListRepository(
                loadDirectories, provider.GetRequiredService<ILogger<YamlTaskListRepository>>(), storage);
        });

        services.AddSingleton<ITaskRunStore>(_ => CreateTaskRunStore(configuration));
        services.AddSingleton<ITaskExecutionEngine, TaskExecutionEngine>();

        services.AddHostedService<TaskListValidationHostedService>();

        return services;
    }

    private static string[] RequireDirectories(IConfiguration configuration, string key)
    {
        var directories = configuration.GetSection(key).GetChildren()
            .Select(section => section.Value)
            .OfType<string>()
            .ToArray();
        return directories.Length == 0
            ? throw new InvalidOperationException($"Configuration value '{key}' is not set.")
            : directories;
    }

    private static string ExpandHomeDirectory(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;

    private static ITaskRunStore CreateTaskRunStore(IConfiguration configuration)
    {
        var kind = configuration["TaskRunStore"] ?? "memory";

        return kind.ToLowerInvariant() switch
        {
            "memory" => new InMemoryTaskRunStore(),
            "yaml" => new YamlTaskRunStore(ResolveTaskRunStoreFilePath(configuration)),
            _ => throw new InvalidOperationException($"Unknown TaskRunStore '{kind}'. Expected 'memory' or 'yaml'."),
        };
    }

    private static string ResolveTaskRunStoreFilePath(IConfiguration configuration) =>
        configuration["taskrunstore:yaml:file"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sbd.dostuff", "TaskRuns.yaml");
}
