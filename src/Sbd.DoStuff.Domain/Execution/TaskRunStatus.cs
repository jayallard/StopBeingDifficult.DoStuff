namespace Sbd.DoStuff.Domain.Execution;

public enum TaskRunStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
