namespace Sbd.DoStuff.Domain.Tasks;

public sealed class TaskExecutionFailedException(string message) : Exception(message);
