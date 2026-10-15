using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sbd.DoStuff.Domain.DataSources;
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
        // Each assets directory's Modules folder goes on PSModulePath, so task commands can
        // `Import-Module <Name>` the same modules a user can import directly in PowerShell.
        var sources = RequireDirectories(configuration, "FileDataSource:Directories")
            .Select(directory => Path.GetFullPath(ExpandHomeDirectory(directory)))
            .ToArray();
        var assetsDirectories = ExistingSubdirectories(sources, FileDataSource.AssetsFolder);

        var moduleDirectories = assetsDirectories
            .Select(directory => Path.Combine(directory, "Modules"))
            .ToArray();
        services.AddSingleton<IProcessRunner>(_ => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsProcessRunner(moduleDirectories)
            : new UnixProcessRunner(moduleDirectories));

        services.AddSingleton<ITaskFactory, Library.TaskFactory>();

        services.AddSingleton<ITaskLibrary>(provider => new YamlTaskLibrary(
            ExistingSubdirectories(sources, FileDataSource.TaskLibraryFolder),
            assetsDirectories,
            provider.GetRequiredService<ILogger<YamlTaskLibrary>>()));
        services.AddSingleton<ITaskListRepository>(provider =>
        {
            // New public lists are saved under the first source, new private lists under the last.
            var storage = new TaskListStorageOptions(
                PublicDirectory: Path.Combine(sources[0], FileDataSource.TaskListsFolder),
                PrivateDirectory: Path.Combine(sources[^1], FileDataSource.TaskListsFolder));

            return new YamlTaskListRepository(
                ExistingSubdirectories(sources, FileDataSource.TaskListsFolder),
                provider.GetRequiredService<ILogger<YamlTaskListRepository>>(), storage);
        });

        foreach (var source in sources)
        {
            services.AddSingleton<IDataSource>(provider => new FileDataSource(
                source, provider.GetRequiredService<ITaskLibrary>(), provider.GetRequiredService<ITaskListRepository>()));
        }

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

    // Every subfolder is optional: a data source only contributes the kinds of data it has.
    private static string[] ExistingSubdirectories(IEnumerable<string> sources, string name) =>
        sources.Select(source => Path.Combine(source, name)).Where(Directory.Exists).ToArray();

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
        ExpandHomeDirectory(configuration["taskrunstore:yaml:file"] ?? "~/.sbd/TaskRuns.yaml");
}
