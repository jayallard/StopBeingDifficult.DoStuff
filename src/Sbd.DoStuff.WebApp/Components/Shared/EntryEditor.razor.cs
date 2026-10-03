using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.WebApp.Editing;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class EntryEditor : IDisposable
{
    /// <summary>Run history tag for test runs, so they never show up as runs of the real list.</summary>
    private const string TestListId = "_test";

    [Parameter, EditorRequired] public EntryEditModel Entry { get; set; } = null!;
    [Parameter, EditorRequired] public ListEditModel Model { get; set; } = null!;
    [Parameter, EditorRequired] public string CategoryPath { get; set; } = "";

    /// <summary>Raised when the entry's categories or existence changed, so the whole tree must re-render.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    private EffectiveTaskDefinition? _effective;
    private string? _loadError;
    private string _newCategory = "";
    private string? _categoryError;

    protected override void OnInitialized() => Engine.RunChanged += OnRunChanged;

    protected override void OnParametersSet()
    {
        _effective = null;
        _loadError = null;

        var definition = Library.Find(Entry.TaskId);
        if (definition is null)
        {
            _loadError = $"Unknown task '{Entry.TaskId}'.";
            return;
        }

        try
        {
            _effective = TaskDefinitionResolver.Resolve(definition, Library);
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }
    }

    private void OnRunChanged(TaskRun run)
    {
        if (Entry.TestRun?.RunId == run.RunId)
        {
            InvokeAsync(StateHasChanged);
        }
    }

    private void ToggleExpanded() => Entry.Expanded = !Entry.Expanded;

    // What the task will use if the user doesn't override: pinned by inheritance, else the declared default.
    private string BaseValue(TaskParameterDefinition parameter) =>
        _effective!.PinnedParameterValues.TryGetValue(parameter.Name, out var pinned) ? pinned : parameter.DefaultValue ?? "";

    private string CurrentValue(TaskParameterDefinition parameter) =>
        Entry.Values.TryGetValue(parameter.Name, out var value) ? value : BaseValue(parameter);

    private void SetValue(TaskParameterDefinition parameter, string value)
    {
        // Storing only real overrides keeps the saved YAML minimal and lets later changes to a default flow through.
        if (value == BaseValue(parameter))
        {
            Entry.Values.Remove(parameter.Name);
        }
        else
        {
            Entry.Values[parameter.Name] = value;
        }
    }

    private void ResetValue(TaskParameterDefinition parameter) => Entry.Values.Remove(parameter.Name);

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

    private async Task AddToCategory()
    {
        var category = _newCategory.Trim();
        if (!TaskListValidator.IsValidCategory(category))
        {
            _categoryError = "Use a dot-separated path like cleanup.temp.";
            return;
        }

        _categoryError = null;
        _newCategory = "";
        Model.AddEntryToCategory(Entry, category);
        await OnChanged.InvokeAsync();
    }

    private async Task RemoveFromCategory(string category)
    {
        Model.RemoveEntryFromCategory(Entry, category);
        await OnChanged.InvokeAsync();
    }

    private async Task Remove()
    {
        // Remove from this category only; the task leaves the list once its last category is gone.
        if (!Model.RemoveEntryFromCategory(Entry, CategoryPath))
        {
            Model.RemoveEntry(Entry);
        }

        await OnChanged.InvokeAsync();
    }

    private void Test()
    {
        Entry.TestError = null;
        Entry.Expanded = Entry.Expanded || _effective is null;

        if (_effective is null)
        {
            Entry.TestError = _loadError;
            return;
        }

        try
        {
            var values = TaskParameterResolver.Resolve(_effective, Entry.Values);
            var task = TaskFactory.Create(_effective, values);
            Entry.TestRun = Engine.StartRun(task, TestListId);
        }
        catch (Exception ex)
        {
            Entry.TestRun = null;
            Entry.TestError = ex.Message;
        }
    }

    private void CancelTest()
    {
        if (Entry.TestRun is not null)
        {
            Engine.TryCancelRun(Entry.TestRun.RunId);
        }
    }

    private void ClearTest()
    {
        Entry.TestRun = null;
        Entry.TestError = null;
    }

    public void Dispose() => Engine.RunChanged -= OnRunChanged;
}
