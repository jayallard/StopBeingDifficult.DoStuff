namespace Sbd.DoStuff.Domain.Lists;

/// <summary>Where a task list is stored: the user's profile folder, or the shared (source-controlled) data folder.</summary>
public enum TaskListScope
{
    Public,
    Private,
}
