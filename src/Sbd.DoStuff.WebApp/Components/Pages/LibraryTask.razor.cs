using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class LibraryTask
{
    /// <summary>Run history tag for runs started straight from the library, so they never show up as runs of a real list.</summary>
    private const string LibraryListId = "_library";

    [Parameter] public string LibraryId { get; set; } = "";
    [Parameter] public string TaskId { get; set; } = "";

    private EffectiveTaskDefinition? _effective;
    private string _libraryName = "";
    private string? _loadError;
    private readonly Dictionary<string, string> _values = new();
    private TaskRun? _run;
    private string? _runError;

    protected override void OnParametersSet()
    {
        _effective = null;
        _loadError = null;
        _values.Clear();
        _run = null;
        _runError = null;

        var group = Library.GetGroups().FirstOrDefault(g => g.Id == LibraryId);
        var definition = group?.Tasks.FirstOrDefault(t => t.Id == TaskId);
        if (group is null || definition is null)
        {
            return;
        }

        _libraryName = group.Name;
        try
        {
            _effective = TaskDefinitionResolver.Resolve(definition, Library);
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }
    }

    protected override void OnInitialized() => Engine.RunChanged += OnRunChanged;

    private void OnRunChanged(TaskRun run)
    {
        if (_run?.RunId == run.RunId)
        {
            InvokeAsync(StateHasChanged);
        }
    }

    // What the task will use if the user doesn't override: pinned by inheritance, else the declared default.
    private string BaseValue(TaskParameterDefinition parameter) =>
        _effective!.PinnedParameterValues.TryGetValue(parameter.Name, out var pinned) ? pinned : parameter.DefaultValue ?? "";

    private string CurrentValue(TaskParameterDefinition parameter) =>
        _values.TryGetValue(parameter.Name, out var value) ? value : BaseValue(parameter);

    private void SetValue(TaskParameterDefinition parameter, string value)
    {
        if (value == BaseValue(parameter))
        {
            _values.Remove(parameter.Name);
        }
        else
        {
            _values[parameter.Name] = value;
        }
    }

    private static string InputClass(bool locked, TaskParameterDefinition parameter, string value)
    {
        const string baseClass = "mt-1 w-full rounded-md border px-2 py-1.5 text-sm focus:outline-none";
        if (locked)
        {
            return $"{baseClass} cursor-not-allowed border-slate-200 bg-slate-100 text-slate-500";
        }

        return parameter.Required && string.IsNullOrWhiteSpace(value)
            ? $"{baseClass} border-red-300 bg-white focus:border-red-500"
            : $"{baseClass} border-slate-300 bg-white focus:border-indigo-500";
    }

    private void Run()
    {
        _runError = null;

        try
        {
            var values = TaskParameterResolver.Resolve(_effective!, _values);
            var task = TaskFactory.Create(_effective!, values);
            _run = Engine.StartRun(task, LibraryListId);
        }
        catch (Exception ex)
        {
            _run = null;
            _runError = ex.Message;
        }
    }

    private void Cancel()
    {
        if (_run is not null)
        {
            Engine.TryCancelRun(_run.RunId);
        }
    }

    public void Dispose() => Engine.RunChanged -= OnRunChanged;
}
