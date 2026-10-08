namespace Sbd.DoStuff.Domain.Library;

/// <summary>
/// The definitions loaded from one library YAML file. Id is the file name without extension
/// (unique across the library only when file names are; later files with the same name get a numeric suffix).
/// </summary>
public sealed record TaskLibraryGroup(string Id, string Name, IReadOnlyList<TaskDefinition> Tasks);
