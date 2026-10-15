namespace Sbd.DoStuff.Domain.DataSources;

/// <summary>
/// A place the app's data comes from. Today that is only file directories (<see cref="FileDataSource"/>);
/// other stores (a database, say) can implement this to show up in the same inventory.
/// </summary>
public interface IDataSource
{
    /// <summary>The kind of store, e.g. "File".</summary>
    string Type { get; }

    /// <summary>Where this source lives (a directory path, a connection name, ...).</summary>
    string Location { get; }

    /// <summary>Everything this source currently provides.</summary>
    IReadOnlyList<DataItem> GetItems();

    /// <summary>
    /// The text of an item this source listed, or null if it isn't one of its items.
    /// This is the only way item content is read, so a viewer can't reach anything the source didn't list.
    /// </summary>
    string? ReadText(DataItem item);
}
