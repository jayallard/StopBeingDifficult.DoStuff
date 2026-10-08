using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Sbd.DoStuff.Domain.Library;

namespace Sbd.DoStuff.WebApp.Components.Layout;

public partial class NavMenu : IDisposable
{
    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => InvokeAsync(StateHasChanged);

    // Keep the library holding the open task expanded.
    private bool IsCurrent(TaskLibraryGroup group) =>
        Navigation.ToBaseRelativePath(Navigation.Uri).StartsWith($"library/{group.Id}/", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;
}
