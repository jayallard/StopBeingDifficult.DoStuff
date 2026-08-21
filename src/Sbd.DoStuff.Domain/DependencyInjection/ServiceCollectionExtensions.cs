using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IProcessRunner>(_ => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsProcessRunner()
            : new UnixProcessRunner());

        services.AddSingleton<ITaskFactory, Library.TaskFactory>();

        services.AddSingleton<ITaskLibrary>(_ => new YamlTaskLibrary(RequireDirectory(configuration, "TaskLibrary:Directory")));
        services.AddSingleton<ITaskListRepository>(_ => new YamlTaskListRepository(RequireDirectory(configuration, "TaskLists:Directory")));

        services.AddSingleton<ITaskRunStore>(_ => CreateTaskRunStore(configuration));
        services.AddSingleton<ITaskExecutionEngine, TaskExecutionEngine>();

        services.AddHostedService<TaskListValidationHostedService>();

        return services;
    }

    private static string RequireDirectory(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException($"Configuration value '{key}' is not set.");

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
