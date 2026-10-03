using System.Text.RegularExpressions;
using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.Domain.Lists;

public static partial class TaskListValidator
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string? id) => !string.IsNullOrEmpty(id) && IdPattern().IsMatch(id);

    public static bool IsValidCategory(string category) =>
        category.Split('.').All(segment => segment.Trim().Length > 0);

    /// <summary>Returns human-readable problems; empty when the list is valid.</summary>
    public static IReadOnlyList<string> Validate(TaskListDefinition list, ITaskLibrary library)
    {
        var errors = new List<string>();

        if (!IsValidId(list.Id))
        {
            errors.Add("Id is required and may contain only letters, digits, '.', '_' and '-'.");
        }

        if (string.IsNullOrWhiteSpace(list.Name))
        {
            errors.Add("Name is required.");
        }

        foreach (var entry in list.Entries)
        {
            foreach (var category in entry.Categories)
            {
                if (!IsValidCategory(category))
                {
                    errors.Add($"Task '{entry.TaskId}': category '{category}' is not a valid dot-separated path.");
                }
            }

            var error = ValidateEntry(entry, library);
            if (error is not null)
            {
                errors.Add($"Task '{entry.TaskId}': {error}");
            }
        }

        return errors;
    }

    /// <summary>Returns an error message for the entry, or null when its task and parameter values resolve.</summary>
    public static string? ValidateEntry(TaskListEntry entry, ITaskLibrary library)
    {
        var definition = library.Find(entry.TaskId);
        if (definition is null)
        {
            return $"unknown task id '{entry.TaskId}'.";
        }

        try
        {
            var effective = TaskDefinitionResolver.Resolve(definition, library);
            TaskParameterResolver.Resolve(effective, entry.ParameterValues);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
