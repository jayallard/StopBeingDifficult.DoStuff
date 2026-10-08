namespace Sbd.DoStuff.Domain.Library;

/// <summary>Tells the UI what kind of input a parameter needs; values are always carried as strings.</summary>
public enum TaskParameterType
{
    Text,
    MultilineText,
    /// <summary>A path to a file; the UI offers a Browse button (typing a path still works).</summary>
    FilePath
}
