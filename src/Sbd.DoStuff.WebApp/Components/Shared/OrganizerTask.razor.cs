using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.Domain.Icons;
using Sbd.DoStuff.Domain.Library;
using Sbd.DoStuff.Domain.Lists;
using Sbd.DoStuff.WebApp.Editing;

namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class OrganizerTask
{
    [Parameter, EditorRequired] public EntryEditModel Entry { get; set; } = null!;
    [Parameter] public bool CanMoveUp { get; set; }
    [Parameter] public bool CanMoveDown { get; set; }
    [Parameter] public EventCallback OnMoveUp { get; set; }
    [Parameter] public EventCallback OnMoveDown { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback OnClone { get; set; }

    /// <summary>Categories this task can be moved to with the Move to… picker.</summary>
    [Parameter] public IEnumerable<OrganizerNode> Destinations { get; set; } = [];

    /// <summary>Raised with the chosen category's id.</summary>
    [Parameter] public EventCallback<string> OnMoveTo { get; set; }

    /// <summary>Raised after an edit was applied to the entry, so the page can persist it.</summary>
    [Parameter] public EventCallback OnSaved { get; set; }

    private EffectiveTaskDefinition? _effective;
    private string? _loadError;
    private bool _expanded;
    private bool _editing;
    private bool _confirmDelete;
    private string? _editError;

    // Edits are held here until Save, so Cancel leaves the entry untouched.
    private string _name = "";
    private string _notes = "";
    private string _icon = "";

    [Inject] private TaskIconCatalog Icons { get; set; } = null!;

    private string InheritedIcon => _effective?.Icon ?? Icons.DefaultIcon;
    private bool _disabled;
    private string _disabledReason = "";
    private Dictionary<string, string> _draft = [];

    private string DefaultName => _effective?.Name ?? Entry.TaskId;
    private string Title => string.IsNullOrWhiteSpace(Entry.Name) ? DefaultName : Entry.Name;

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

    private async Task OnMoveToChanged(ChangeEventArgs e)
    {
        if (e.Value?.ToString() is { Length: > 0 } id)
        {
            await OnMoveTo.InvokeAsync(id);
        }
    }

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        if (!_expanded)
        {
            _editing = false;
        }
    }

    private void StartEdit()
    {
        _expanded = true;
        _editing = true;
        _editError = null;
        _name = Entry.Name ?? "";
        _notes = Entry.Notes ?? "";
        _icon = Entry.Icon ?? InheritedIcon;
        _disabled = Entry.Disabled;
        _disabledReason = Entry.DisabledReason ?? "";
        _draft = _effective?.Parameters.ToDictionary(p => p.Name, CurrentValue) ?? [];
    }

    private void CancelEdit()
    {
        _editing = false;
        _editError = null;
    }

    private async Task Save()
    {
        var values = new Dictionary<string, string>(Entry.Values);
        if (_effective is not null)
        {
            foreach (var parameter in _effective.Parameters)
            {
                if (_effective.NonOverridableParameterNames.Contains(parameter.Name))
                {
                    continue;
                }

                // Only real overrides are stored, so later changes to a default still flow through.
                var value = _draft.GetValueOrDefault(parameter.Name, "");
                if (value == BaseValue(parameter))
                {
                    values.Remove(parameter.Name);
                }
                else
                {
                    values[parameter.Name] = value;
                }
            }
        }

        var candidate = new TaskListEntry(Entry.TaskId, Entry.Categories, values.Count == 0 ? null : values);
        _editError = TaskListValidator.ValidateEntry(candidate, Library);
        if (_editError is not null)
        {
            return;
        }

        Entry.Name = string.IsNullOrWhiteSpace(_name) ? null : _name.Trim();
        Entry.Notes = string.IsNullOrWhiteSpace(_notes) ? null : _notes.Trim();
        Entry.Icon = string.IsNullOrWhiteSpace(_icon) || _icon.Trim() == InheritedIcon ? null : _icon.Trim();
        Entry.Disabled = _disabled;
        Entry.DisabledReason = string.IsNullOrWhiteSpace(_disabledReason) ? null : _disabledReason.Trim();
        Entry.Values.Clear();
        foreach (var (name, value) in values)
        {
            Entry.Values[name] = value;
        }

        _editing = false;
        await OnSaved.InvokeAsync();
    }

    // What the task will use if the entry doesn't override: pinned by inheritance, else the declared default.
    private string BaseValue(TaskParameterDefinition parameter) =>
        _effective!.PinnedParameterValues.TryGetValue(parameter.Name, out var pinned) ? pinned : parameter.DefaultValue ?? "";

    private string CurrentValue(TaskParameterDefinition parameter) =>
        Entry.Values.TryGetValue(parameter.Name, out var value) ? value : BaseValue(parameter);

    private static bool IsChecked(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private void SetChecked(TaskParameterDefinition parameter, ChangeEventArgs e) =>
        _draft[parameter.Name] = e.Value is true ? "true" : "false";

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
