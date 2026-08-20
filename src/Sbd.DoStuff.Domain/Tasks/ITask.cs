namespace Sbd.DoStuff.Domain.Tasks;

public interface ITask
{
    string Id { get; }
    string Name { get; }
    string? Description { get; }

    Task RunAsync(ITaskExecutionContext context, CancellationToken cancellationToken);
}
