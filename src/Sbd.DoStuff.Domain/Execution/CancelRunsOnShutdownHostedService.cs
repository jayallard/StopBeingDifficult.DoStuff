using Microsoft.Extensions.Hosting;

namespace Sbd.DoStuff.Domain.Execution;

/// <summary>
/// On a graceful app stop, cancels active runs so their process trees are killed rather than orphaned.
/// A hard kill of the app can't be intercepted; <see cref="YamlTaskRunStore"/> reconciles those on next start.
/// </summary>
internal sealed class CancelRunsOnShutdownHostedService(ITaskExecutionEngine engine) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => engine.CancelAllRunsAsync(TimeSpan.FromSeconds(5));
}
