namespace Sbd.DoStuff.Domain.DataSources;

public enum DataItemKind
{
    /// <summary>A file of task definitions.</summary>
    TaskLibrary,

    /// <summary>A task list.</summary>
    TaskList,

    /// <summary>A script, module or other file that tasks refer to.</summary>
    Asset,
}
