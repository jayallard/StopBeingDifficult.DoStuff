namespace Sbd.DoStuff.Domain.Library;

public sealed class InvalidParameterOptionException(
    string taskId, string parameterName, string value, IReadOnlyList<string> options)
    : Exception($"Task '{taskId}' parameter '{parameterName}' value '{value}' is not one of: {string.Join(", ", options)}.");
