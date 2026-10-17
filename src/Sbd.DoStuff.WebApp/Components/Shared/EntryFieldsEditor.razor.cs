using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Execution;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.WebApp.Editing;

namespace Sbd.DoStuff.WebApp.Components.Shared;

/// <summary>Name, notes and parameter-override inputs for one list entry being edited.</summary>
public partial class EntryFieldsEditor
{
    [Parameter, EditorRequired] public EntryEditModel Entry { get; set; } = null!;
    [Parameter, EditorRequired] public EffectiveTaskDefinition Effective { get; set; } = null!;

    // What the task will use if the user doesn't override: pinned by inheritance, else the declared default.
    private string BaseValue(TaskParameterDefinition parameter) =>
        Effective.PinnedParameterValues.TryGetValue(parameter.Name, out var pinned) ? pinned : parameter.DefaultValue ?? "";

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

    private static bool IsChecked(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private void SetChecked(TaskParameterDefinition parameter, ChangeEventArgs e) =>
        SetValue(parameter, e.Value is true ? "true" : "false");

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
}
