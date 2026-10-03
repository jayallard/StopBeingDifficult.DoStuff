namespace Sbd.DoStuff.Domain.Lists;

/// <summary>The directories new/edited task lists are written to, by scope.</summary>
public sealed record TaskListStorageOptions(string PublicDirectory, string PrivateDirectory)
{
    public string DirectoryFor(TaskListScope scope) => scope == TaskListScope.Private ? PrivateDirectory : PublicDirectory;
}
