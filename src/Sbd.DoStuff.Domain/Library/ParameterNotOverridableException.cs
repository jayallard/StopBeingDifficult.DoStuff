namespace Sbd.DoStuff.Domain.Library;

public sealed class ParameterNotOverridableException(string taskId, string parameterName)
    : Exception($"Task '{taskId}' parameter '{parameterName}' cannot be overridden.");
