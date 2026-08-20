namespace Sbd.DoStuff.Domain.Library;

public sealed class TaskDefinitionCycleException(IReadOnlyList<string> chain)
    : Exception($"Task definition base chain is broken or cyclical: {string.Join(" -> ", chain)}");
