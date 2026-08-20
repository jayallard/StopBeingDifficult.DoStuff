using Microsoft.Extensions.Hosting;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;

namespace Sbd.DoStuff.Domain.Validation;

/// <summary>
/// Fails app startup with one clear message if the library/lists are broken, instead of the
/// user discovering a broken reference by clicking "Run" in the browser.
/// </summary>
internal sealed class TaskListValidationHostedService(ITaskListRepository lists, ITaskLibrary library) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        foreach (var definition in library.GetAll())
        {
            try
            {
                TaskDefinitionResolver.Resolve(definition, library);
            }
            catch (Exception ex)
            {
                errors.Add($"Task definition '{definition.Id}': {ex.Message}");
            }
        }

        foreach (var list in lists.GetAll())
        {
            foreach (var entry in list.Entries)
            {
                var definition = library.Find(entry.TaskId);
                if (definition is null)
                {
                    errors.Add($"Task list '{list.Id}' references unknown task id '{entry.TaskId}'.");
                    continue;
                }

                try
                {
                    var effective = TaskDefinitionResolver.Resolve(definition, library);
                    TaskParameterResolver.Resolve(effective, entry.ParameterValues);
                }
                catch (Exception ex)
                {
                    errors.Add($"Task list '{list.Id}', task '{entry.TaskId}': {ex.Message}");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Task library/list validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
