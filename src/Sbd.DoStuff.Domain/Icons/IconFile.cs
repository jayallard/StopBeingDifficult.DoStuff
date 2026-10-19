namespace Sbd.DoStuff.Domain.Icons;

/// <param name="Name">The icon name as used in YAML (no extension).</param>
/// <param name="FileName">The file on disk, with extension.</param>
/// <param name="Path">The full path of the file.</param>
public sealed record IconFile(string Name, string FileName, string Path);
