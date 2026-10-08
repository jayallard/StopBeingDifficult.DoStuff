using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.WebApp.Services;

namespace Sbd.DoStuff.WebApp.Components.Shared;

/// <summary>A text box for a file path, with an optional Browse button that opens a native file dialog.</summary>
public partial class FilePathInput
{
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public string InputClass { get; set; } = "";
    [Parameter] public bool Disabled { get; set; }

    private bool _browsing;
    private string? _error;

    private Task OnTyped(ChangeEventArgs e) => ValueChanged.InvokeAsync(e.Value?.ToString() ?? string.Empty);

    private async Task Browse()
    {
        _error = null;
        _browsing = true;
        try
        {
            var path = await FilePicker.PickFileAsync(Value);
            if (path is not null)
            {
                await ValueChanged.InvokeAsync(path);
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _browsing = false;
        }
    }
}
