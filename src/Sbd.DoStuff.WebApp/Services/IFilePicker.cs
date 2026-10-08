namespace Sbd.DoStuff.WebApp.Services;

/// <summary>
/// Lets the UI pick a file on the machine the server runs on. Blazor Server runs on the user's own
/// machine here, so a native dialog opened by the server is the dialog the user sees.
/// </summary>
public interface IFilePicker
{
    bool IsSupported { get; }

    /// <summary>Returns the chosen path, or null if the dialog was cancelled.</summary>
    Task<string?> PickFileAsync(string? currentPath, CancellationToken cancellationToken = default);
}
