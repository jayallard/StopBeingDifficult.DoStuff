namespace Sbd.DoStuff.Domain.Library;

public sealed class MissingRequiredParameterException(string taskId, string parameterName)
    : Exception($"Task '{taskId}' is missing required parameter '{parameterName}'.");
