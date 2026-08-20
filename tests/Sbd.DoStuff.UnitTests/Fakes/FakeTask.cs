using Sbd.DoStuff.Domain.Tasks;

namespace Sbd.DoStuff.UnitTests.Fakes;

internal sealed class FakeTask(
    string id = "fake-task",
    string name = "Fake Task",
    Func<ITaskExecutionContext, CancellationToken, Task>? run = null) : ITask
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string? Description => null;

    public Task RunAsync(ITaskExecutionContext context, CancellationToken cancellationToken) =>
        run?.Invoke(context, cancellationToken) ?? Task.CompletedTask;
}
