using Microsoft.AspNetCore.Components;
using Sbd.DoStuff.WebApp.Services;

namespace Sbd.DoStuff.WebApp.Components.Shared;

/// <summary>A text box for a file path, with a Browse button that navigates the server's file system.</summary>
public partial class FilePathInput
{
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public string InputClass { get; set; } = "";
    [Parameter] public bool Disabled { get; set; }

    private DirectoryListing? _listing;

    private Task OnTyped(ChangeEventArgs e) => ValueChanged.InvokeAsync(e.Value?.ToString() ?? string.Empty);

    private void ToggleBrowser() => _listing = _listing is null ? FileSystem.List(Value) : null;

    private void Close() => _listing = null;

    private void GoUp()
    {
        if (_listing?.Parent is { } parent)
        {
            _listing = FileSystem.List(parent);
        }
    }

    private async Task Select(FileSystemItem item)
    {
        if (item.IsDirectory)
        {
            _listing = FileSystem.List(item.FullPath);
            return;
        }

        await ValueChanged.InvokeAsync(item.FullPath);
        _listing = null;
    }
}
