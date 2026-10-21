using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Sbd.DoStuff.WebApp.Components.Pages;

public partial class LibraryNew
{
    private string _name = "";
    private string _storageDirectory = "";
    private string? _error;

    protected override void OnInitialized() =>
        _storageDirectory = Library.StorageDirectories.FirstOrDefault() ?? "";

    // A task library folder's parent is the configured data directory, which is what the user recognises.
    private static string DirectoryLabel(string storageDirectory) =>
        Path.GetDirectoryName(storageDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? storageDirectory;

    private static string Slug(string name) =>
        Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private void Create()
    {
        _error = null;
        var slug = Slug(_name);
        if (slug.Length == 0)
        {
            _error = "Enter a name.";
            return;
        }

        try
        {
            Library.CreateGroup(slug, _storageDirectory);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _error = ex.Message;
            return;
        }

        Navigation.NavigateTo("library");
    }
}
