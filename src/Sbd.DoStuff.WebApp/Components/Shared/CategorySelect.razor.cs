using Microsoft.AspNetCore.Components;

namespace Sbd.DoStuff.WebApp.Components.Shared;

/// <summary>A drop-down of every existing category (dot-notation paths, empty or populated) with an option to type a new one.</summary>
public partial class CategorySelect
{
    private const string NewMarker = "\u0001new";

    [Parameter, EditorRequired] public IEnumerable<string> Categories { get; set; } = [];
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public string Placeholder { get; set; } = "Select category…";
    [Parameter] public string InputClass { get; set; } = "w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none";

    private bool _creating;
    private bool _initialised;

    protected override void OnParametersSet()
    {
        // A starting value that isn't an existing category can only be typed, so begin in text mode.
        if (!_initialised)
        {
            _initialised = true;
            _creating = !string.IsNullOrEmpty(Value)
                && !Categories.Contains(Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task OnSelected(ChangeEventArgs e)
    {
        var selected = e.Value?.ToString() ?? "";
        if (selected == NewMarker)
        {
            _creating = true;
            await ValueChanged.InvokeAsync("");
            return;
        }

        await ValueChanged.InvokeAsync(selected);
    }

    private Task OnTyped(ChangeEventArgs e) => ValueChanged.InvokeAsync(e.Value?.ToString() ?? "");

    private async Task BackToList()
    {
        _creating = false;
        await ValueChanged.InvokeAsync("");
    }
}
