using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.Domain.Lists;

public static class CategoryTreeBuilder
{
    public static CategoryNode Build(IEnumerable<TaskListEntry> entries, ITaskLibrary library)
    {
        var root = new CategoryNode(string.Empty, string.Empty);

        foreach (var entry in entries)
        {
            var view = TryResolve(entry, library);
            if (view is null)
            {
                continue;
            }

            foreach (var category in entry.Categories)
            {
                var node = root;
                var pathSoFar = string.Empty;

                foreach (var segment in category.Split('.'))
                {
                    pathSoFar = pathSoFar.Length == 0 ? segment : $"{pathSoFar}.{segment}";
                    node = node.GetOrAddChild(segment, pathSoFar);
                }

                node.AddTask(view);
            }
        }

        return root;
    }

    // Dangling task ids or resolution failures (cycle, missing required parameter) are
    // skipped here rather than thrown — startup validation (TaskListValidationHostedService)
    // is the authoritative fail-fast check, so this path should never actually trigger
    // in a correctly-validated app.
    private static TaskListEntryView? TryResolve(TaskListEntry entry, ITaskLibrary library)
    {
        var definition = library.Find(entry.TaskId);
        if (definition is null)
        {
            return null;
        }

        try
        {
            var effective = TaskDefinitionResolver.Resolve(definition, library);
            var values = TaskParameterResolver.Resolve(effective, entry.ParameterValues);
            return new TaskListEntryView(effective, values);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
