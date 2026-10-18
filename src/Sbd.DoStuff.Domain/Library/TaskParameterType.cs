namespace Sbd.DoStuff.Domain.Library;

/// <summary>Tells the UI what kind of input a parameter needs; values are always carried as strings.</summary>
public enum TaskParameterType
{
    Text,
    MultilineText,
    /// <summary>A path to a file; the UI offers a Browse button (typing a path still works).</summary>
    FilePath,
    /// <summary>A checkbox; the value is carried as the string "true" or "false" (a missing value means false).</summary>
    Boolean,
    /// <summary>A single-select dropdown over the parameter's <c>Options</c>, shown in the order defined; the first option is the default unless one is declared.</summary>
    SelectList
}
