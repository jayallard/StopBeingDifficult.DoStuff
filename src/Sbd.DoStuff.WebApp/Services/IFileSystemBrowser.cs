namespace Sbd.DoStuff.WebApp.Services;

/// <summary>One folder or file in a <see cref="DirectoryListing"/>.</summary>
public sealed record FileSystemItem(string Name, string FullPath, bool IsDirectory);

/// <summary>
/// The contents of one folder. <see cref="Path"/> is empty for the top level (drive list on Windows,
/// <c>/</c> contents elsewhere is returned as a normal folder). <see cref="Parent"/> is null at the top.
/// </summary>
public sealed record DirectoryListing(string Path, string? Parent, IReadOnlyList<FileSystemItem> Items, string? Error = null);

/// <summary>
/// Lets the UI navigate the file system of the machine the server runs on. Blazor Server runs on the user's
/// own machine here, so the server can see everything the user can.
/// </summary>
public interface IFileSystemBrowser
{
    /// <summary>
    /// Lists <paramref name="path"/>. A blank or non-existent path falls back to its nearest existing
    /// ancestor, or to the top level (drives) if none exists. If <paramref name="path"/> names a file,
    /// its folder is listed.
    /// </summary>
    DirectoryListing List(string? path);
}
