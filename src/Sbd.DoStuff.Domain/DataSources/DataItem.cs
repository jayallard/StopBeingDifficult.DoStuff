namespace Sbd.DoStuff.Domain.DataSources;

/// <summary>One thing a data source provides: a library file, a task list or an asset.</summary>
/// <param name="Kind">What sort of data this is.</param>
/// <param name="Name">Where it sits within its source (a relative path for files).</param>
/// <param name="Location">The full path, URI or key that identifies it in its source.</param>
/// <param name="Provides">Ids of the definitions or lists loaded from it; empty for assets.</param>
/// <param name="SizeBytes">Size, when the source knows it.</param>
/// <param name="Scope">Whether a task list is public or private; null for other kinds.</param>
public sealed record DataItem(
    DataItemKind Kind,
    string Name,
    string Location,
    IReadOnlyList<string> Provides,
    long? SizeBytes = null,
    string? Scope = null);
